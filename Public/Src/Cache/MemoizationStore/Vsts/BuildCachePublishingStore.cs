﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts.Internal;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
using Microsoft.VisualStudio.Services.WebApi;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Publishes metadata to the BuildCache service.
    /// </summary>
    public class BuildCachePublishingStore : StartupShutdownSlimBase, IPublishingStore
    {
        private ResourcePoolV2<(BuildCacheServiceConfiguration config, string pat), ICachePublisher>? _publishers;
        private readonly IAbsFileSystem _fileSystem;

        private readonly SemaphoreSlim _publishingGate;

        /// <summary>
        /// The publishing store needs somewhere to get content from in case it needs to publish a
        /// content hash list's contents. This should point towards some locally available cache.
        /// </summary>
        private readonly IContentStore _contentSource;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BuildCachePublishingStore));

        /// <nodoc />
        public BuildCachePublishingStore(IContentStore contentSource, IAbsFileSystem fileSystem, int concurrencyLimit)
        {
            _contentSource = contentSource;
            _fileSystem = fileSystem;

            _publishingGate = new SemaphoreSlim(concurrencyLimit);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _publishers = new ResourcePoolV2<(BuildCacheServiceConfiguration config, string pat), ICachePublisher>(
                context, new ResourcePoolConfiguration(), configAndPat => CreatePublisher(configAndPat.config, configAndPat.pat, context));

            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _publishers?.Dispose();
            return BoolResult.SuccessTask;
        }

        /// <nodoc />
        protected virtual ICachePublisher CreatePublisher(BuildCacheServiceConfiguration config, string pat, Context context)
        {
            var credHelper = new VsoCredentialHelper();
            var credFactory = new VssCredentialsFactory(new VssBasicCredential(new NetworkCredential(string.Empty, pat)));

            var cache = BuildCacheCacheFactory.Create(
                _fileSystem,
                context.Logger,
                credFactory,
                config,
                writeThroughContentStoreFunc: null);

            cache.StartupAsync(context).GetAwaiter().GetResult().ThrowIfFailure();

            var sessionResult = cache.CreateSession(context, name: null, ImplicitPin.None).ThrowIfFailure();
            var session = sessionResult.Session;

            Contract.Check(session is BuildCacheSession)?.Assert($"Session should be an instance of {nameof(BuildCacheSession)}. Actual type: {session.GetType()}");

            // Skip initialization because the resource pool does it for us.
            return (BuildCacheSession)session;
        }

        /// <inheritdoc />
        public Result<IPublishingSession> CreateSession(Context context, PublishingCacheConfiguration config, string pat)
        {
            if (config is not BuildCacheServiceConfiguration buildCacheConfig)
            {
                return new Result<IPublishingSession>($"Configuration is not a {nameof(BuildCacheServiceConfiguration)}. Actual type: {config.GetType().FullName}");
            }

            return new Result<IPublishingSession>(new BuildCachePublishingSession(buildCacheConfig, pat, this));
        }

        internal Task<BoolResult> PublishContentHashListAsync(
            Context context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList,
            BuildCacheServiceConfiguration config,
            string pat,
            CancellationToken token)
        {
            if (!StartupCompleted)
            {
                return Task.FromResult(new BoolResult("Startup must be completed before attempting to publish."));
            }

            var operationContext = new OperationContext(context, token);

            Tracer.Debug(operationContext, $"Enqueueing publish request for StrongFingerprint=[{fingerprint}], CHL=[{contentHashList.ToTraceString()}]");

            return _publishingGate.GatedOperationAsync(
                (timeSpentWaiting, gateCount) =>
                {
                    ContentHashList? hashListInRemote = null;
                    return operationContext.PerformOperationAsync(
                        Tracer,
                        () =>
                        {
                            return _publishers!.UseAsync(context, (config, pat), async publisherWrapper =>
                            {
                                var publisher = await publisherWrapper.LazyValue;
                                var remotePinResults = await Task.WhenAll(await publisher.PinAsync(operationContext, contentHashList.ContentHashList.Hashes, token));
                                var missingFromRemote = remotePinResults
                                    .Where(r => !r.Item.Succeeded)
                                    .Select(r => contentHashList.ContentHashList.Hashes[r.Index])
                                    .ToArray();

                                if (missingFromRemote.Length > 0)
                                {
                                    await PushToRemoteAsync(operationContext, missingFromRemote, publisher).ThrowIfFailure();
                                }

                                var addOrGetResult = await publisher.AddOrGetContentHashListAsync(operationContext, fingerprint, contentHashList, token).ThrowIfFailure();
                                hashListInRemote = addOrGetResult.ContentHashListWithDeterminism.ContentHashList;

                                return BoolResult.Success;
                            });
                        },
                        traceOperationStarted: false,
                        extraEndMessage: result =>
                            $"Added=[{result.Succeeded && hashListInRemote is null}], " +
                            $"StrongFingerprint=[{fingerprint}], " +
                            $"ContentHashList=[{contentHashList.ToTraceString()}], " +
                            $"TimeSpentWaiting=[{timeSpentWaiting}], " +
                            $"GateCount=[{gateCount}]");
                },
                token);
        }

        private async Task<BoolResult> PushToRemoteAsync(OperationContext context, IReadOnlyList<ContentHash> hashes, ICachePublisher publisher)
        {
            var sessionResult = _contentSource.CreateReadOnlySession(context, context.TracingContext.ToString()!, ImplicitPin.None).ThrowIfFailure();
            var session = sessionResult.Session!;
            await session.StartupAsync(context).ThrowIfFailure();

            try
            {
                var pinResults = await Task.WhenAll(await session.PinAsync(context, hashes, context.Token));
                var missingFromLocal = pinResults.Where(r => !r.Item.Succeeded);
                if (missingFromLocal.Any())
                {
                    return new BoolResult($"Not all contents of the content hash list are available in the cache. Missing hashes: {string.Join(", ", missingFromLocal.Select(m => hashes[m.Index].ToShortString()))}");
                }

                // TODO: concurrency?
                foreach (var hash in hashes)
                {
                    var streamResult = await session.OpenStreamAsync(context, hash, context.Token).ThrowIfFailure();
                    var stream = streamResult.Stream;

                    var putStreamResult = await publisher.PutStreamAsync(context, hash, stream, context.Token).ThrowIfFailure();
                }

                return BoolResult.Success;
            }
            finally
            {
                await session.ShutdownAsync(context).ThrowIfFailure();
            }
        }
    }
}
