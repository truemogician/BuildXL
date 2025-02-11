// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Common;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// A factory for creating <see cref="IServer"/> and <see cref="IClient"/> instances.
    /// </summary>
    public interface IIpcProvider
    {
        /// <summary>
        /// Renders given moniker to a connection string (that can be passed to <see cref="GetClient"/> and <see cref="GetServer"/>).
        /// </summary>
        /// <remarks>
        /// This method MUST be <strong>stable</strong>, i.e., over time, it must
        /// always return the same connetion string for all monikers with the same ID.
        /// </remarks>
        string RenderConnectionString(IpcMoniker moniker);

        /// <summary>
        /// Creates an <see cref="IServer"/> instance given
        /// a moniker and a server configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on an IPC moniker.
        /// </remarks>
        IServer GetServer(string connectionString, IServerConfig config);

        /// <summary>
        /// Creates an <see cref="IClient"/> instance given
        /// a moniker and a client configuration.
        /// </summary>
        /// <remarks>
        /// The <paramref name="connectionString"/> must be a string obtained by calling
        /// <see cref="RenderConnectionString"/> on a moniker previously returned by this provider.
        /// </remarks>
        IClient GetClient(string connectionString, IClientConfig config);
    }
}
