using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Inceptum.Rest
{
    class UriPool : IEnumerable<Uri>
    {
        private readonly Stopwatch m_Stopwatch = Stopwatch.StartNew();
      
        internal PoolUri[] Uris { get; private set; }
        private readonly long m_FailTimeout;
        private readonly long m_Timeout;


        public UriPool(long failTimeout,long timeout,params Uri[] addresses)
        {
            if(addresses.Length==0)
                throw new ArgumentException("addresses should contain at least 1 uri");
            m_Timeout = timeout;
            m_FailTimeout = failTimeout;
            Uris= addresses.Select(s => new PoolUri(s)
            {
                LastAttemptFinish = -failTimeout*10-random(1000),
                LastAttemptStart = -failTimeout*10-random(1000)
            }).ToArray();
        }


        public IEnumerator<Uri> GetEnumerator()
        {
            return new UriPoolEnumerator(this, m_Timeout);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static int random(int maxValue)
        {
            var buf = Guid.NewGuid().ToByteArray();
            var rnd = BitConverter.ToInt32(buf, 4) % maxValue;
            return rnd; 
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal Uri GetUri()
        {
            var length = Uris.Length;
            if (length == 1) return Uris.First().Uri;

            var now = m_Stopwatch.ElapsedMilliseconds;

            //Return address to be tested with oldest last attempt time. (If there is one)
            var toBeTested = Uris.OrderBy(a => a.LastAttemptFinish)
                .FirstOrDefault(a => !a.IsValid && now - a.LastAttemptFinish > m_FailTimeout && !a.IsBeingTested);

            if (toBeTested != null)
            {
                toBeTested.IsBeingTested = true;
                toBeTested.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
                return toBeTested.Uri;
            }

            //Then use valid random  addresses in order
            var validUri = Uris.Where(a => a.IsValid).OrderBy(s => random(Uris.Length * 1000)).FirstOrDefault();

            if (validUri!=null)
            {
                validUri.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
                return validUri.Uri;
            }



            //If tested uri does not exist or fails and there is no valid uri, take invalid addrsses , oredered by last attempt time 
            var invalidUri =
                Uris
                    .OrderBy(a => a.IsBeingTested) //take uris not being tested first
                    .ThenBy(a => a.LastAttemptStart)
                    .ThenBy(s => random(Uris.Length*1000))
                    .ToArray().First();

            invalidUri.IsBeingTested = true;
            invalidUri.LastAttemptStart = m_Stopwatch.ElapsedMilliseconds;
            return invalidUri.Uri;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ReportAttempt(Uri uri, bool success)
        {
            var poolUri = Uris.FirstOrDefault(u=>u.Uri==uri);
            if(poolUri==null)
                throw new InvalidOperationException(string.Format("{0} can not be reported as {1} since it does not belong to pool",uri, success?"valid":"invalid"));
            poolUri.IsValid = success;
            poolUri.LastAttemptFinish = m_Stopwatch.ElapsedMilliseconds;
            poolUri.IsBeingTested = false;
        }
    }
}