using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Inceptum.Rest
{
    class UriPool : IEnumerable<PoolUri>
    {
        private readonly Stopwatch m_Stopwatch = Stopwatch.StartNew();
        private readonly long m_FailTimeout;
        private readonly long m_PoolEnumerationTimeout;
        internal PoolUri[] Uris { get; private set; }

        public UriPool(long failTimeout, long poolEnumerationTimeout, params Uri[] addresses)
        {
            if (addresses == null || addresses.Length == 0)
                throw new ArgumentException("addresses should contain at least 1 uri");

            m_PoolEnumerationTimeout = poolEnumerationTimeout;
            m_FailTimeout = failTimeout;
            Uris = addresses.Select(s => new PoolUri(s)
            {
                IsValid = true,
                LastAttemptFinish = -failTimeout * 10 - random(1000),
                LastAttemptStart = -failTimeout * 10 - random(1000)
            }).ToArray();
        }

        public IEnumerator<PoolUri> GetEnumerator()
        {
            return new UriPoolEnumerator(this, m_PoolEnumerationTimeout);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static int random(int maxValue)
        {
            using (var randomizer = new RNGCryptoServiceProvider())
            {
                var buf = new byte[16];
                randomizer.GetNonZeroBytes(buf);
                var rnd = BitConverter.ToInt32(buf, 4) % maxValue;
                return rnd;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal PoolUri GetUri()
        {
            var length = Uris.Length;
            if (length == 1) return Uris.First();

            var now = m_Stopwatch.ElapsedMilliseconds;

            //Return address to be tested with oldest last attempt time. (If there is one)
            var toBeTested = Uris.OrderBy(a => a.LastAttemptFinish)
                .FirstOrDefault(a => !a.IsValid && now - a.LastAttemptFinish > m_FailTimeout && !a.IsBeingTested);

            if (toBeTested != null)
            {
                toBeTested.IsBeingTested = true;
                toBeTested.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
                return toBeTested;
            }

            //Then use valid random  addresses in order
            var validUri = Uris.Where(a => a.IsValid).OrderBy(s => random(Uris.Length * 1000)).FirstOrDefault();

            if (validUri != null)
            {
                validUri.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
                return validUri;
            }

            //If tested uri does not exist or fails and there is no valid uri, take invalid addrsses , oredered by last attempt time 
            var invalidUri =
                Uris
                    .OrderBy(a => a.IsBeingTested) //take uris not being tested first
                    .ThenBy(a => a.LastAttemptStart)
                    .ThenBy(s => random(Uris.Length * 1000))
                    .First();

            invalidUri.IsBeingTested = true;
            invalidUri.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
            return invalidUri;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ReportAttempt(Uri uri, bool success)
        {
            var poolUri = Uris.FirstOrDefault(u => u.Uri == uri);
            if (poolUri == null)
                throw new InvalidOperationException(string.Format("{0} can not be reported as {1} since it does not belong to pool", uri, success ? "valid" : "invalid"));
            poolUri.IsValid = success;
            poolUri.LastAttemptFinish = m_Stopwatch.ElapsedMilliseconds;
            poolUri.IsBeingTested = false;
        }
    }
}