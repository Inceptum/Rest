using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Inceptum.Rest
{
    public abstract class RestClientBase:IDisposable
    {
        private long m_RequestsInProgress = 0;
        private bool m_IsDisposed = false;
        private readonly string m_UserAgentName;
        private readonly TimeSpan m_Timeout;
        private readonly ConcurrentDictionary<Tuple<Uri, CultureInfo>, ConcurrentQueue<HttpClient>> m_ClientsCache = new ConcurrentDictionary<Tuple<Uri, CultureInfo>, ConcurrentQueue<HttpClient>>();
        private readonly Func<HttpMessageHandler> m_MessageHandler;
        private readonly UriPool m_UriPool;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestClientBase"/> class.
        /// </summary>
        /// <param name="addresses">The addresses pool.</param>
        /// <param name="failTimeout">Timeoute address should be excluded from pool after rquest to the address fails (may be violated if all addresses in pool are excluded).</param>
        /// <param name="farmRequestTimeout">The farm request timeout. During this timeout <see cref="RestClientBase"/> will reuqest addresses in the pool till gets valid response (HTTP status >=500)</param>
        /// <param name="singleAddressTimeout">The single address timeout.</param>
        /// <param name="handlerFactory">The handler factory.</param>
        /// <exception cref="System.ArgumentNullException">addresses</exception>
        /// <exception cref="System.ArgumentException">
        /// Can not be empty;addresses
        /// </exception>
        protected RestClientBase(  string[] addresses, int failTimeout=15000,int farmRequestTimeout=120000, long singleAddressTimeout = 60000, Func<HttpMessageHandler> handlerFactory = null)
        {
            if (addresses == null) throw new ArgumentNullException("addresses");
            if (addresses.Length == 0) throw new ArgumentException("Can not be empty", "addresses");
            var addressesList = new List<Uri>();
            foreach (var address in addresses)
            {
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException(string.Format("addresses must be not empty string but one was '{0}", address));

                if (!Uri.IsWellFormedUriString(address, UriKind.Absolute))
                    throw new ArgumentException(string.Format("addresses must be valid absolute uri but one was '{0}", address));


                addressesList.Add(new Uri(address, UriKind.Absolute));
            }

            m_UriPool = new UriPool(failTimeout, farmRequestTimeout, addressesList.ToArray());

            if (handlerFactory == null)
                m_MessageHandler = () => new HttpClientHandler();
            else
                m_MessageHandler = handlerFactory;


         

            m_Timeout = TimeSpan.FromMilliseconds(singleAddressTimeout);

            m_UserAgentName = GetType().Name + "-" + GetType().Assembly.GetName().Version;
        }

        private HttpClientHost getClient(Uri baseUri, CultureInfo cultureInfo)
        {
            HttpClient client;
            var queue = m_ClientsCache.GetOrAdd(Tuple.Create(baseUri, cultureInfo), _ => new ConcurrentQueue<HttpClient>());

            //Console.WriteLine("[{3}] {0}\t{1}\t{2}", baseUri, m_ClientsCache.Count, m_RequestsInProgress, Thread.CurrentThread.ManagedThreadId);

            if (queue.TryDequeue(out client) == false)
            {
                client = CreateClient(baseUri, cultureInfo);
            }
            Interlocked.Increment(ref m_RequestsInProgress);
            return new HttpClientHost(client, () =>
            {
                Interlocked.Decrement(ref m_RequestsInProgress);
                queue.Enqueue(client);
            });

        }


        protected virtual HttpClient CreateClient(Uri baseUri, CultureInfo cultureInfo)
        {
            var client = new HttpClient(m_MessageHandler()) { BaseAddress = baseUri };
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(cultureInfo.Name));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("utf-8"));
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(m_UserAgentName);
            client.Timeout = m_Timeout;
            return client;
        }


        protected Task<TResult> GetData<TResult>(Uri relativeUri, Func<string, TResult> res, CultureInfo cultureInfo)
        {
            return SendAsync(() => new HttpRequestMessage(HttpMethod.Get, relativeUri), res, cultureInfo);
        }
        protected async Task<TResult> SendAsync<TResult>(Func<HttpRequestMessage> requestFactory,Func<string, TResult> res, CultureInfo cultureInfo)
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException("");

            foreach (var baseUri in m_UriPool)
            {
                using (var host = getClient(baseUri, cultureInfo))
                {
                    var client = host.Client;
                    bool success = false;
                    try
                    {
                        var request = requestFactory();

                        if (request == null)
                            throw new InvalidOperationException("can not send null request");
                        if (request.RequestUri.IsAbsoluteUri)
                            throw new InvalidOperationException("request should have relative uri");
                        try
                        {

                            var response = await client.SendAsync(request).ConfigureAwait(false);
                            if (response.StatusCode <HttpStatusCode.InternalServerError)
                            {
                                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                success = true;
                                return res(content);
                            }
                        }
                        catch (Exception e)
                        {
#if DEBUG
                            Console.WriteLine(e.Message);
#endif
                            //TODO: logging
                        }
                    }
                    finally
                    {
                        m_UriPool.ReportAttempt(baseUri, success);
                    }
                }
            }
            var tcs = new TaskCompletionSource<TResult>();
            tcs.SetCanceled();
            return await tcs.Task;
        }

        public void Dispose()
        {
            m_IsDisposed = true;
            while (Interlocked.Read(ref m_RequestsInProgress)>0)
            {
                Thread.Sleep(100);
            }
            foreach (var q in m_ClientsCache.Select(x => x.Value))
            {
                HttpClient result;
                while (q.TryDequeue(out result))
                {
                    result.Dispose();
                }
            }
            m_ClientsCache.Clear();
        }
    }
}