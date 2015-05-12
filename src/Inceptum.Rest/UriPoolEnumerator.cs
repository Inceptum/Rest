using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Inceptum.Rest
{
    class UriPoolEnumerator : IEnumerator<PoolUri>
    {
        private readonly UriPool m_Pool;
        private readonly Stopwatch m_Stopwatch=new Stopwatch();
        private long m_Start;
        private readonly long m_Timeout;

        public UriPoolEnumerator(UriPool pool, long timeout)
        {
            m_Timeout = timeout;
            m_Start = m_Stopwatch.ElapsedMilliseconds;
            m_Pool = pool;
        }

        public bool MoveNext()
        {
            if(!m_Stopwatch.IsRunning)
                m_Stopwatch.Start();
            return m_Stopwatch.ElapsedMilliseconds - m_Start < m_Timeout;
        }

        public void Reset()
        {
            m_Start = m_Stopwatch.ElapsedMilliseconds;
        }

        public PoolUri Current
        {
            get
            {
                return m_Pool.GetUri();
            }
        }

        object IEnumerator.Current
        {
            get { return Current; }
        }

        public void Dispose()
        {
            m_Stopwatch.Stop();
        }
    }
}