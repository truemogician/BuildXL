// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <summary>
    ///     Provides a thread-safe source of randomness
    /// </summary>
    public static class ThreadSafeRandom
    {
        /// <summary>
        ///     Each thread gets its own Random
        /// </summary>
        private static readonly ThreadLocal<Random> TlsRand =
            new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref RndSeed)));

        /// <summary>
        ///     Seed that all threads increment so that many threads starting in parallel still get different different results
        /// </summary>
        private static int RndSeed = Environment.TickCount;

        /// <summary>
        ///     Gets thread-safe randomness generator
        /// </summary>
        public static Random Generator => TlsRand.Value;

        /// <nodoc />
        public static void SetSeed(int seed)
        {
            TlsRand.Value = new Random(seed);
        }

        /// <summary>
        ///     Construct an array of random bytes in a thread-safe manner.
        /// </summary>
        /// <param name="count">Number of bytes to generate</param>
        /// <returns>The array of random bytes.</returns>
        public static byte[] GetBytes(int count)
        {
            Contract.Requires(count >= 0);
            var bytes = new byte[count];
            Generator.NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// Create a random string of the desired length in a thread-safe manner.
        /// </summary>
        public static string RandomAlphanumeric(int length)
        {
            const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(Characters, length)
              .Select(s => s[Generator.Next(s.Length)]).ToArray());
        }

        /// <nodoc />
        public static int Uniform(int minValue, int maxValue)
        {
            return Generator.Next(minValue, maxValue);
        }

        /// <nodoc />
        public static double ContinuousUniform(double minValue, double maxValue)
        {
            return minValue + (maxValue - minValue) * Generator.NextDouble();
        }

        /// <summary>
        ///     Shuffle array in place.
        /// </summary>
        /// <param name="array">Array to be shuffled</param>
        public static void Shuffle<T>(IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = Generator.Next(n--);
                T temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
        }
    }
}
