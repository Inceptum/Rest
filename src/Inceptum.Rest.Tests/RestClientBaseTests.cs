using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace Inceptum.Rest.Tests
{

    public class RestClient : RestClientBase
    {
        public RestClient(string[] addresses, int failTimeout = 15000, int farmRequestTimeout = 120000, long singleAddressTimeout = 60000, Func<HttpMessageHandler> handlerFactory = null)
            : base(addresses, failTimeout, farmRequestTimeout, singleAddressTimeout, handlerFactory)
        {
        }

        public Task<string> SendAsync(Func<HttpRequestMessage> requestFactory, CultureInfo cultureInfo)
        {
            return SendAsync(requestFactory, s => s, cultureInfo);
        }

        public Task<string> GetData(Uri relativeUri, CultureInfo cultureInfo)
        {
            return GetData(relativeUri, s => s, cultureInfo);
        }
    }

    public class TestController : ApiController
    {
        static TestController()
        {
            FailingPorts=new List<int>();
        }

        public static List<int> FailingPorts { get; set; }
        [HttpGet, Route("ok")]
        public string Get()
        {
            var port = Request.RequestUri.Port;
            var fail = FailingPorts.Contains(port);
            Console.WriteLine("{0}\t{1}\t{2}",DateTime.Now,port, fail?503:200);
            Thread.Sleep(10);
            if(fail)
                throw new  Exception("Sorry. Out of order.");
            return port.ToString();
        }
    }

    [TestFixture]
    public class RestClientBaseTests
    {
        private HttpSelfHostServer[] m_Servers;
        private const int SERVERS_COUNT = 3;

        [ SetUp]
        public void FixtureSetUp()
        {
            TestController.FailingPorts.Clear();
            Thread.CurrentThread.CurrentUICulture = Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            m_Servers = Enumerable.Range(1, SERVERS_COUNT).Select(i => new HttpSelfHostServer(createApiConfiguration("http://localhost:" + (1000 + i)))).ToArray();
            Task.WaitAll(m_Servers.Select(s => s.OpenAsync()).ToArray());
            Task.WaitAll(
            Enumerable.Range(1, SERVERS_COUNT)
                .Select(i => "http://localhost:" + (1000 + i))
                .Select(uri => new RestClient(new[] {uri}))
                .Select(c =>
                {
                    var r = c.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture);
                    c.Dispose();
                    return r;
                }).ToArray());
            Console.WriteLine("==============================================");
 
        }
        
        [ TearDown]
        public void FixtureTearDown()
        {
            Task.WaitAll(m_Servers.Select(s => s.CloseAsync()).ToArray());
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


        [Test]
        public async void PerformanceTest()
        {



            using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i =>  "http://localhost:" + (1000 + i)).ToArray()))
            {
                Console.WriteLine(await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture));

                Stopwatch sw = Stopwatch.StartNew();

                var tasks = Enumerable.Range(1, 1000)
                    .Select(i =>
                    {
                        Thread.Sleep(0);
                        return testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture);

                    })
                    .ToArray();
                Console.WriteLine(Thread.CurrentThread.ManagedThreadId);
                Console.WriteLine(sw.ElapsedMilliseconds);
                Task.WaitAll(tasks);
                Console.WriteLine(sw.ElapsedMilliseconds);

                var ports = tasks.Select(t => t.Result).Where(t => t != null);
                var dict = new Dictionary<string, int> { { "\"1001\"", 0 }, { "\"1002\"", 0 }, { "\"1003\"", 0 } };
                foreach (var port in ports)
                {
                    dict[port]++;
                }

                foreach (var pair in dict.OrderBy(p => p.Key))
                {
                    Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
                }
            }
        }


        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public async void RequestUriShouldBeRelativeTest()
        {
            using (var testRestClient =new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray()))
            {
                for (int j = 1; j <= SERVERS_COUNT; j++)
                {
                    TestController.FailingPorts.Add(1000+j);
                }
                int i = 0;
                await testRestClient.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, i++ == 0 ? new Uri("/ok", UriKind.RelativeOrAbsolute) : new Uri("http://localhost:1001/ok", UriKind.RelativeOrAbsolute)), CultureInfo.CurrentUICulture);
            }
        }

        [Test]
        public async void AddressesSelcetionTest()
        {
            using (var testRestClient = new RestClient(Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray()))
            {
                TestController.FailingPorts.Add(1001);
                var tasks = Enumerable.Range(1, 100)
                    .Select(i =>
                    {
                        Thread.Sleep(10);
                        return testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture);

                    })
                    .ToArray();
                Task.WaitAll(tasks);

                var ports = tasks.Select(t=>t.Result).Where(t=>t!=null);
                var dict= new Dictionary<string, int> { { "\"1001\"", 0 }, { "\"1002\"", 0 }, { "\"1003\"", 0 } };
                foreach (var port in ports)
                {
                    dict[port]++;
                }

                foreach (var pair in dict.OrderBy(p => p.Key))
                {
                    Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
                }
            }
        }


        [Test]
        public async void RequestTillTimoutEndsTest()
        {
            TestController.FailingPorts.AddRange(new []{1001,1002,1003});
            var addresses = Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray();
            using (var testRestClient = new RestClient(addresses,farmRequestTimeout:1500))
            {
                var e=new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(state =>
                {
                    e.WaitOne();
                    Thread.Sleep(1000);
                    TestController.FailingPorts.Clear();
                });
                var sw = Stopwatch.StartNew();
                e.Set(); 
                await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture);
                sw.Stop();
                Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(900));
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500));
            }
        }
        
        [Test]
        public async void TaskCancelledExceptionIsTheownOnFarmRequestTimeoutReachTest()
        {
            TestController.FailingPorts.AddRange(new []{1001,1002,1003});
            var addresses = Enumerable.Range(1, SERVERS_COUNT).Select(i => "http://localhost:" + (1000 + i)).ToArray();
            using (var testRestClient = new RestClient(addresses,farmRequestTimeout:600))
            {
                var sw = Stopwatch.StartNew();
                TaskCanceledException ex=null;
                try
                {
                    await testRestClient.GetData(new Uri("/ok", UriKind.Relative), CultureInfo.CurrentUICulture);
                }
                catch (TaskCanceledException e)
                {
                    ex = e;
                }
                Assert.That(ex, Is.Not.Null);
                sw.Stop();
                Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(600));
            }
        }



      
    }
}
