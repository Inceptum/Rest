using System;
using System.Net.Http;

namespace Inceptum.Rest
{
    class HttpClientHost : IDisposable
    {
        private readonly Action m_Dispose;

        public HttpClientHost(HttpClient client, Action dispose)
        {
            m_Dispose = dispose;
            Client = client;
        }

        public HttpClient Client { get; private set; }

        public void Dispose()
        {
            m_Dispose();
        }
    }
}