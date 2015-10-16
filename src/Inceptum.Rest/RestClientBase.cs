using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Inceptum.Rest
{
    public class NodeRequestResult
    {
        public NodeRequestResult(HttpRequestMessage request)
        {
            Request = request;
        }

        public Uri Uri
        {
            get { return Request == null ? null : Request.RequestUri; }
        }

        public HttpRequestMessage Request { get; set; }
        public Exception Exception { get; set; }
        public HttpResponseMessage Response { get; set; }

    }

    public class FarmRequestTimeoutException : Exception
    {


        public FarmRequestTimeoutException()
        {
        }

        public FarmRequestTimeoutException(string message, IEnumerable<NodeRequestResult> attempts = null)
            : base(message)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }

        public FarmRequestTimeoutException(string message, Exception innerException, IEnumerable<NodeRequestResult> attempts = null)
            : base(message, innerException)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }

        protected FarmRequestTimeoutException(SerializationInfo info, StreamingContext context, IEnumerable<NodeRequestResult> attempts = null)
            : base(info, context)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }

        public NodeRequestResult[] Attempts { get; private set; }

    }


    public abstract class RestClientBase : IDisposable
    {
        private readonly int m_DelayTimeout;
        private long m_RequestsInProgress;
        private bool m_IsDisposed;
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
        /// <param name="delayTimeout">The delay between fail requests (when all addresses are excluded from pool)</param>
        /// <param name="handlerFactory">The handler factory.</param>
        /// <exception cref="System.ArgumentNullException">addresses</exception>
        /// <exception cref="System.ArgumentException">
        /// Can not be empty;addresses
        /// </exception>
        protected RestClientBase(string[] addresses, int failTimeout = 15000, int farmRequestTimeout = 120000, long singleAddressTimeout = 60000, int delayTimeout = 5000, Func<HttpMessageHandler> handlerFactory = null)
        {
            if (addresses == null) throw new ArgumentNullException("addresses");
            if (addresses.Length == 0) throw new ArgumentException("Can not be empty", "addresses");

            validateIsGreaterThan(0, failTimeout, "failTimeout");
            validateIsGreaterThan(0, farmRequestTimeout, "farmRequestTimeout");
            validateIsGreaterThan(0, singleAddressTimeout, "singleAddressTimeout");
            validateIsGreaterThan(-1, delayTimeout, "delayTimeout");

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
            m_DelayTimeout = delayTimeout;

            m_UserAgentName = GetType().Name + "-" + GetType().Assembly.GetName().Version;
        }

        static void validateIsGreaterThan(long max, long value, string paramName)
        {
            if (value <= max)
            {
                throw new ArgumentOutOfRangeException(paramName, value, string.Format("{0} must be greater than {1}", paramName, max));
            }
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

        [Obsolete("This method is obsolete and is a subject to be removed. Use overload with CancellationToken instead.")]
        protected async Task<RestResponse<TResult>> GetData<TResult>(Uri relativeUri, CultureInfo cultureInfo)
        {
            return await GetData<TResult>(relativeUri, cultureInfo, CancellationToken.None);
        }

        protected Task<RestResponse<TResult>> GetData<TResult>(Uri relativeUri, CultureInfo cultureInfo, CancellationToken cancellationToken, IEnumerable<MediaTypeFormatter> formatters = null)
        {
            return SendAsync<TResult>(() => new HttpRequestMessage(HttpMethod.Get, relativeUri), cultureInfo, cancellationToken, formatters);
        }

        protected async Task<RestResponse<TResult>> SendAsync<TResult>(Func<HttpRequestMessage> requestFactory, CultureInfo cultureInfo, CancellationToken cancellationToken, IEnumerable<MediaTypeFormatter> formatters = null)
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException("");

            if (formatters == null)
                formatters = new MediaTypeFormatterCollection();

            var attempts = new List<NodeRequestResult>();
            foreach (var baseUri in m_UriPool)
            {
                if (!baseUri.IsValid)
                    await Task.Delay(m_DelayTimeout);

                using (var host = getClient(baseUri.Uri, cultureInfo))
                {
                    var client = host.Client;
                    var success = false;
                    try
                    {
                        var request = requestFactory();

                        if (request == null)
                            throw new InvalidOperationException("can not send null request");
                        if (attempts.Any(r => ReferenceEquals(request, r.Request)))
                            throw new InvalidOperationException("requestFactory request factory should produce new HttpRequestMessage instance each time it is called");
                        var attempt = new NodeRequestResult(request);
                        attempts.Add(attempt);

                        if (request.RequestUri.IsAbsoluteUri)
                            throw new InvalidOperationException("request should have relative uri");

                        try
                        {
                            attempt.Response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                            if (attempt.Response.StatusCode < HttpStatusCode.InternalServerError)
                            {
                                var content = default(TResult);
                                if (attempt.Response.Content != null && attempt.Response.Content.Headers.ContentLength > 0)
                                    try
                                    {
                                        content = await attempt.Response.Content.ReadAsAsync<TResult>(formatters, cancellationToken).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        /* If response can't be deserialized to TResult, it mean's that error ocuured, and caller should decide what to do */
                                    }
                                success = true;
                                return new RestResponse<TResult>
                                {
                                    Response = content,
                                    Headers = attempt.Response.Content.Headers,
                                    StatusCode = attempt.Response.StatusCode,
                                    RawResponse = attempt.Response
                                };
                            }
                        }
                        catch (OperationCanceledException e)
                        {
                            attempt.Exception = e;

                            if (cancellationToken.IsCancellationRequested)
                                throw new FarmRequestTimeoutException("Request was cancelled by consuming code", attempts);
#if DEBUG
                            Console.WriteLine("Timeout: " + e.Message);
#endif 
                        }
                        catch (Exception e)
                        {
                            attempt.Exception = e;
#if DEBUG
                            Console.WriteLine(e.Message);
#endif
                        }
                    }
                    finally
                    {
                        m_UriPool.ReportAttempt(baseUri.Uri, success);
                    }
                }
            }

            throw new FarmRequestTimeoutException("Failed to get valid request form nodes in pool within timeout", attempts);
        }

        public void Dispose()
        {
            m_IsDisposed = true;
            while (Interlocked.Read(ref m_RequestsInProgress) > 0)
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