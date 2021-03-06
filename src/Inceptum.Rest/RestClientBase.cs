﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Inceptum.Rest
{
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
        /// <param name="failTimeout">Timeout address should be excluded from pool after rquest to the address fails (may be violated if all addresses in pool are excluded).</param>
        /// <param name="farmRequestTimeout">The farm request timeout. During this timeout <see cref="RestClientBase"/> will reuqest addresses in the pool till gets valid response (HTTP status >=500)</param>
        /// <param name="singleAddressTimeout">The single address timeout.</param>
        /// <param name="delayTimeout">The delay before retring after all addresses has failed and are excluded from pool</param>
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

        protected Task<RestResponse<TResult>> GetData<TResult>(Uri relativeUri, CultureInfo cultureInfo, CancellationToken cancellationToken = default(CancellationToken), IEnumerable<MediaTypeFormatter> formatters = null)
        {
            return SendAsync<TResult>(() => new HttpRequestMessage(HttpMethod.Get, relativeUri), cultureInfo, cancellationToken, formatters);
        }

        protected async Task<RestResponse<TResult>> SendAsync<TResult>(Func<HttpRequestMessage> requestFactory, CultureInfo cultureInfo, CancellationToken cancellationToken, IEnumerable<MediaTypeFormatter> formatters = null)
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException("The rest client is dispossed");

            var mediaTypeFormatters = formatters == null ? new MediaTypeFormatterCollection().ToArray() : formatters.ToArray();

            var attempts = new List<NodeRequestResult>();
            foreach (var baseUri in m_UriPool)
            {
                if (!baseUri.IsValid)
                    await Task.Delay(m_DelayTimeout, cancellationToken);

                using (var host = getClient(baseUri.Uri, cultureInfo))
                {
                    var client = host.Client;
                    var success = false;
                    try
                    {
                        var request = requestFactory();

                        if (request == null)
                            throw new InvalidOperationException("Can't send null request");

                        if (attempts.Any(r => ReferenceEquals(request, r.Request)))
                            throw new InvalidOperationException("Request factory should produce new HttpRequestMessage instance each time it is called");

                        var attempt = new NodeRequestResult(request);
                        attempts.Add(attempt);

                        if (request.RequestUri.IsAbsoluteUri)
                            throw new InvalidOperationException("Request should have relative uri");

                        try
                        {
                            attempt.Response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                            if (attempt.Response.StatusCode < HttpStatusCode.InternalServerError)
                            {
                                var content = default(TResult);

                                if (attempt.Response.Content != null && attempt.Response.Content.Headers.ContentLength > 0)
                                {
                                    try
                                    {
                                        // Note[tv]: load request's content into memory to avoid empirically observed situation when 
                                        // multiple calls to Read...Async for non-buffered content resulted in locking the caller for infinite time.
                                        await attempt.Response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                                        content = await attempt.Response.Content.ReadAsAsync<TResult>(mediaTypeFormatters, cancellationToken).ConfigureAwait(false);
                                    }
                                    catch (Exception e)
                                    {
                                        /* If response can't be deserialized to TResult, it mean's that error occured, and caller should decide what to do */
                                        writeLine(e.Message);
                                    }
                                }

                                success = true;
                                return new RestResponse<TResult>
                                {
                                    Response = content,
                                    Headers = attempt.Response.Content != null ? attempt.Response.Content.Headers : new StreamContent(Stream.Null).Headers,
                                    StatusCode = attempt.Response.StatusCode,
                                    RawResponse = attempt.Response
                                };
                            }
                        }
                        catch (OperationCanceledException e)
                        {
                            attempt.Exception = e;
                            if (cancellationToken.IsCancellationRequested)
                            {
                                success = true;
                                throw new FarmRequestTimeoutException("Request was cancelled by consuming code", e, attempts);
                            }
                            writeLine("Timeout: " + e.Message);
                        }
                        catch (Exception e)
                        {
                            attempt.Exception = e;
                            writeLine(e.Message);
                        }
                    }
                    finally
                    {
                        m_UriPool.ReportAttempt(baseUri.Uri, success);
                    }
                }
            }

            var errorMessage = buildFarmRequestTimeoutErrorMessage(attempts);

            throw new FarmRequestTimeoutException(errorMessage, attempts);
        }

        private string buildFarmRequestTimeoutErrorMessage(IReadOnlyCollection<NodeRequestResult> attempts)
        {
            if (attempts == null) throw new ArgumentNullException("attempts");

            var sb = new StringBuilder()
                .AppendFormat("Failed to get valid request from nodes in pool within {0} timeout ({1} attempts were made)", TimeSpan.FromMilliseconds(m_UriPool.PoolEnumerationTimeout), attempts.Count)
                .AppendLine()
                .AppendFormat("Pool state:");
            foreach (var address in m_UriPool.Uris)
            {
                sb.AppendLine().AppendFormat("\t{0}", address);
            }

            sb
                .AppendLine()
                .AppendFormat("FailTimeout: {0}ms", m_UriPool.FailTimeout) // (Timeout address should be excluded from pool after request to the address fails)
                .AppendLine()
                .AppendFormat("FarmRequestTimeout: {0}ms", m_UriPool.PoolEnumerationTimeout) // (The farm request timeout)
                .AppendLine()
                .AppendFormat("DelayTimeout: {0}ms", m_DelayTimeout) // (The delay before retring after all addresses has failed and were excluded from pool)
                .AppendLine();
            var lastAttempt = attempts.LastOrDefault();
            if (lastAttempt != null)
            {
                sb
                    .AppendFormat("Last attempt:")
                    .AppendLine()
                    .AppendFormat("\tUri:  {0}", lastAttempt.Uri)
                    .AppendLine()
                    .AppendFormat("\tRequest:  {0}", lastAttempt.Request == null ? "NO REQUEST" : lastAttempt.Request.ToString().Replace("\n", Environment.NewLine + "\t\t"))
                    .AppendLine()
                    .AppendFormat("\tResponse:  {0}", lastAttempt.Response == null ? "NO RESPONSE" : lastAttempt.Response.ToString().Replace(Environment.NewLine, Environment.NewLine + "\t\t"));
                if (lastAttempt.Exception != null)
                {
                    sb.AppendLine()
                        .AppendFormat("\tException:  {0}", lastAttempt.Exception.ToString().Replace(Environment.NewLine, Environment.NewLine + "\t\t"));
                }
            }
            return sb.ToString();
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

        [Conditional("DEBUG")]
        static void writeLine(string message)
        {
            Console.WriteLine(message);
        }
    }
}