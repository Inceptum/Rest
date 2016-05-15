using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace Inceptum.Rest.Tests
{
    public class TestController : ApiController
    {
        static TestController()
        {
            FailingPorts = new List<int>();
        }

        public static List<int> FailingPorts { get; set; }
        [HttpGet, Route("ok")]
        public string Get()
        {
            var port = Request.RequestUri.Port;
            var fail = FailingPorts.Contains(port);
            Console.WriteLine("{0}\t{1}\t{2}", DateTime.Now, port, fail ? 503 : 200);
            Thread.Sleep(10);
            if (fail)
                throw new Exception("Sorry. Out of order.");
            return port.ToString(CultureInfo.InvariantCulture);
        }

        [HttpGet, Route("delay")]
        public async Task Delay(int seconds)
        {
            Console.WriteLine("Delay for " + seconds + " seconds");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            Console.WriteLine("Unfter delay for " + seconds + " seconds");
        }

        [HttpGet, Route("test")]
        public TestDto GetTestDto()
        {
            //await Task.Delay(TimeSpan.FromMilliseconds(100)); // Emulate some kind of work
            return new TestDto()
            {
                Id = Guid.NewGuid().ToString(),
                Created = DateTime.UtcNow,
                Dictionary = new Dictionary<string, string>()
                {
                    { "ru", "Русский"},
                    {"en", "English"}
                },
                Collection = new[] { "tag1", "tag2" }
            };
        }
    }

    public class TestDto
    {
        public string Id;
        public DateTime Created;
        public string[] Collection = new string[0];
        public Dictionary<string, string> Dictionary = new Dictionary<string, string>();
    }

}