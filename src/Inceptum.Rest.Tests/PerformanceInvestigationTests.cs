#define USE_LOCAL_SERVER

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace Inceptum.Rest.Tests
{
    [TestFixture, Ignore("The tests are intended for performance investigation")]
    public class PerformanceInvestigationTests
    {
#if USE_LOCAL_SERVER
        private const int PORT = 1000;
        private const string HOST = "localhost";
#else
        private const int PORT = 5007;
        private const string HOST = "sr-tls04-s01.test-s02.uniservers.ru";
#endif
        private const int DEFAULT_CALLS_COUNT = 20;
        private static readonly Uri m_BaseUri = new Uri(string.Format("http://{0}:{1}", HOST, PORT));

#if USE_LOCAL_SERVER
        private HttpSelfHostServer m_Server;

        [SetUp]
        public void FixtureSetUp()
        {
            TestController.FailingPorts.Clear();
            m_Server = new HttpSelfHostServer(createApiConfiguration(m_BaseUri.ToString()));
            m_Server.OpenAsync().Wait();
            var r = new RestClient(new[] { m_BaseUri.ToString() })
                .GetObject<JObject>(new Uri("test", UriKind.Relative), CultureInfo.CurrentUICulture, CancellationToken.None)
                .Result;
            Console.WriteLine("==== Local server started at {0} ===", m_BaseUri);
        }

        [TearDown]
        public void FixtureTearDown()
        {
            m_Server.CloseAsync().Wait();
        }

        private static HttpSelfHostConfiguration createApiConfiguration(string baseUrl)
        {
            var config = new HttpSelfHostConfiguration(baseUrl);

            JsonMediaTypeFormatter jsonFormatter = config.Formatters.JsonFormatter;
            jsonFormatter.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            jsonFormatter.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Ignore;
            jsonFormatter.SerializerSettings.Formatting = Formatting.Indented;
            jsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Routes.MapHttpRoute("Default", "api/{controller}", new { controller = "Test" });
            config.MapHttpAttributeRoutes();

            return config;
        }
#endif

        [Test, Description("The test measures data retrieval time with socket call without futher deserialization")]
        public void RawSocketRequest()
        {
            var ip = Dns.GetHostAddresses(HOST)[0];
            var endPoint = new IPEndPoint(ip, PORT);

            Console.WriteLine(@"Host: {0}, ip: {1}", HOST, ip);

            measure(() =>
            {
                var client = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                client.Connect(endPoint);

                string request = string.Format("GET /test HTTP/1.1\r\nHost: {0}\r\n\r\n", HOST);

                byte[] requestBuffer = Encoding.ASCII.GetBytes(request);

                client.Send(requestBuffer);

                byte[] responseBuffer = new byte[5 * 1024 * 1024];

                int bytesRec = client.Receive(responseBuffer);

                //Console.WriteLine(Encoding.UTF8.GetString(responseBuffer, 0, bytesRec));

                client.Shutdown(SocketShutdown.Both);
                client.Close();

            }, DEFAULT_CALLS_COUNT);
        }

        [Test, Description("The test measures data retrieval time with socket call with connection reuse without futher deserialization")]
        public void RawSocketRequestConnectionReuse()
        {
            var ip = Dns.GetHostAddresses(HOST)[0];
            var endPoint = new IPEndPoint(ip, PORT);

            Console.WriteLine(@"Host: {0}, ip: {1}", HOST, ip);

            var client = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            client.Connect(endPoint);

            measure(() =>
            {
                string request = string.Format("GET /test HTTP/1.1\r\nHost: {0}\r\nConnection: Keep-Alive\r\n\r\n", HOST);

                byte[] requestBuffer = Encoding.ASCII.GetBytes(request);

                client.Send(requestBuffer);

                byte[] responseBuffer = new byte[5 * 1024 * 1024];

                int bytesRec = client.Receive(responseBuffer);

                // Console.WriteLine(Encoding.UTF8.GetString(responseBuffer, 0, bytesRec));
            }, DEFAULT_CALLS_COUNT);

            client.Shutdown(SocketShutdown.Both);
            client.Close();
        }

        private static void applyOptimizations()
        {
            // Network and protocol related optimizations
            WebRequest.DefaultWebProxy = null;  // No proxy
            ServicePointManager.DefaultConnectionLimit = 50;    // Outgoing connections limit, default 2
            ServicePointManager.MaxServicePointIdleTime
                = TimeSpan.FromSeconds(3 * 100).Milliseconds; // Timeout to release unused connections, default 100 sec
            ServicePointManager.Expect100Continue = false;

            // Warm up the serializer
            //var none = JsonConvert.DeserializeObject("{ dsl: '' }", typeof(TestDto)); 
        }

        [Test]
        public void PlainWebRequest()
        {
            applyOptimizations();

            // Note[tv]: First call is much slower than following ones
            measure(() =>
            {
                var request = (HttpWebRequest)WebRequest.Create(new Uri(m_BaseUri, "test") + "?_bust=" + DateTime.UtcNow.Ticks);
                request.Method = "GET";
                request.Proxy = null;
                var webResponse = request.GetResponse();
                var stringContent = new StreamReader(webResponse.GetResponseStream()).ReadToEnd();
                var dto = (TestDto)JsonConvert.DeserializeObject(stringContent, typeof(TestDto));
                Assert.IsNotNull(dto.Id);
            }, DEFAULT_CALLS_COUNT);
        }

        [Test]
        public void PlainWebRequestDetailedMetrics()
        {
            applyOptimizations();

            //List<Task> tasks = new List<Task>();

            for (int i = 0; i < 3; i++)
            {
                // Todo[tv]: uncomment to start in parallel
                //tasks.Add(Task.Factory.StartNew(() =>
                //{
                var request = (HttpWebRequest)WebRequest.Create(new Uri(m_BaseUri, "test") + "?_bust=" + DateTime.UtcNow.Ticks);
                request.Method = "GET";
                request.Proxy = null;
                WebResponse webResponse = null;
                string stringContent = "";
                TestDto dto = null;

                var network = measure(() => webResponse = request.GetResponse());
                var dataRead = measure(() => stringContent = new StreamReader(webResponse.GetResponseStream()).ReadToEnd());
                var dataParse = measure(() =>
                {
                    dto = (TestDto)JsonConvert.DeserializeObject(stringContent, typeof(TestDto));
                    Assert.IsNotNull(dto.Id);
                });
                var total = network + dataRead + dataParse;

                Assert.IsNotNull(dto);
                var sb = new StringBuilder();
                sb.AppendFormat(@"Network call: {0}ms ({1})", network, TimeSpan.FromMilliseconds(network))
                    .AppendLine()
                    .AppendFormat(@"Data read: {0}ms ({1})", dataRead, TimeSpan.FromMilliseconds(dataRead))
                    .AppendLine()
                    .AppendFormat(@"Data parse: {0}ms ({1})", dataParse, TimeSpan.FromMilliseconds(dataParse))
                    .AppendLine()
                    .AppendFormat(@"Total: {0}ms ({1})", total, TimeSpan.FromMilliseconds(total))
                    .AppendLine();

                Console.WriteLine(sb.ToString());
                //}));
            }

            //Task.WaitAll(tasks.ToArray());
        }

        [Test]
        public void HttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null
            }) { BaseAddress = m_BaseUri };

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("utf-8"));

            applyOptimizations();

            measure(() =>
            {
                var task = client.GetAsync("test?_bust=" + DateTime.UtcNow.Ticks);
                task.Wait();
                var dataTask = task.Result.Content.ReadAsAsync<TestDto>();
                dataTask.Wait();
                Assert.IsNotNull(dataTask.Result.Id);
            }, DEFAULT_CALLS_COUNT);
        }

        [Test]
        public void IncepumClient()
        {
            var client = new RestClient(new[] { m_BaseUri.ToString() });

            applyOptimizations();

            measure(() =>
            {
                var task = client.GetObject<TestDto>(new Uri("test", UriKind.Relative), CultureInfo.CurrentCulture, CancellationToken.None);
                task.Wait();
                Assert.IsNotNull(task.Result.Response.Id);
            }, DEFAULT_CALLS_COUNT);
        }

        [Test]
        public void IncepumClientParallel()
        {
            var client = new RestClient(new[] { m_BaseUri.ToString() });
            const int calls = 100;
            const int degreeOfParallelism = 15; // Note[tv]: under heavy load performance falls sagnificantly, so 5 is OK, but 15 is already not

            applyOptimizations();

            measureParallel(() =>
            {
                var task = client.GetObject<TestDto>(new Uri("test", UriKind.Relative), CultureInfo.CurrentCulture, CancellationToken.None);
                task.Wait();
                Assert.IsNotNull(task.Result.Response);
            }, calls, degreeOfParallelism);

            Console.WriteLine();
            Console.WriteLine(@"----SECOND RUN (The client is warmed up)----");
            measureParallel(() =>
            {
                var task = client.GetObject<TestDto>(new Uri("test", UriKind.Relative), CultureInfo.CurrentCulture, CancellationToken.None);
                task.Wait();
                Assert.IsNotNull(task.Result.Response);
            }, calls, degreeOfParallelism);
        }

        private static long measure(Action action)
        {
            var sw = new Stopwatch();
            sw.Start();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private static double measure(Action action, int attempts)
        {
            long total = 0;
            for (int i = 1; i < attempts + 1; i++)
            {
                var sw = new Stopwatch();
                sw.Start();
                action();
                sw.Stop();
                total += sw.ElapsedMilliseconds;
                Console.WriteLine(@"Call #{0} finished in {1}ms ({2})", i, sw.ElapsedMilliseconds, TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds));
            }

            var avg = (double)total / attempts;
            Console.WriteLine(@"Average of {0} attempts: {1}ms ({2})", attempts, avg, TimeSpan.FromMilliseconds(avg));

            return avg;
        }

        private static long measureParallel(Action action, int attempts, int degreeOfParallelism = 5)
        {
            long total = 0;

            var actions = Enumerable.Range(0, attempts).Select(i =>
            {
                Action _ = () =>
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    action();
                    sw.Stop();
                    var elapsed = sw.ElapsedMilliseconds;
                    Interlocked.Add(ref total, elapsed);
                    Console.WriteLine(@"Call #{0} finished in {1}ms ({2})", i + 1, elapsed, TimeSpan.FromMilliseconds(elapsed));
                };
                return _;

            });
            Parallel.Invoke(new ParallelOptions
            {
                MaxDegreeOfParallelism = degreeOfParallelism
            }, actions.ToArray());

            var time = (double)total / attempts;
            Console.WriteLine(@"Average of {0} attempts: {1}ms ({2})", attempts, time, TimeSpan.FromMilliseconds(time));

            return total;
        }
    }
}