using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Inceptum.Rest.Tests
{
    [TestFixture]
    public class UriPoolTests
    {

        [Test]
        public void UrisPoolEnumeratorExitsAfterTimeoutTest()
        {
            var pool = new UriPool(200, 200, Enumerable.Range(1, 10).Select(i => new Uri("http://localhost:" + (1000 + i))).ToArray());
            Stopwatch sw =Stopwatch.StartNew();
            foreach (var uri in pool)
            {
                Thread.Sleep(10);
            }
            sw.Stop();
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(250));
        }

        [Test]
        public void AllUrisInPoolAreInitiallyInvalidTest()
        {
            var pool = new UriPool(200,100000, Enumerable.Range(1, 10).Select(i => new Uri("http://localhost:" + (1000 + i))).ToArray());
            Assert.That(pool.Uris.Select(uri => uri.IsValid),Is.All.False,"Some uri were marked as valid before first request");
            Assert.That(pool.Uris.Select(uri => uri.IsBeingTested),Is.All.False,"Some uri were marked as being tested before first request");
        }

        [Test]
        public void AfterErrorIsReportedUriIsExcludedFromRotationForFailTimeoutPeriodTest()
        {
            var uris = Enumerable.Range(1, 10).Select(i => new Uri("http://localhost:" + (1000 + i))).ToArray();
            var pool = new UriPool(200, 100000, uris);
            var uri = new Uri("http://localhost:1001/");
            foreach (var u in uris)
            {
                pool.ReportAttempt(u, u!=uri);    
            }
            
            Stopwatch sw=Stopwatch.StartNew();
            bool gotFailedUri = false;
            while (!gotFailedUri && sw.ElapsedMilliseconds < 400)
            {
                var received = pool.GetUri();
                gotFailedUri = received.Uri == uri;
                pool.ReportAttempt(received.Uri,true);
                Thread.Sleep(10);
            }
            sw.Stop();
            Console.WriteLine("Got uri within {0}ms after it was reported as failed",sw.ElapsedMilliseconds);
            Assert.That(gotFailedUri,Is.True,"Uri was not returned after failtimeout");
            Assert.That(sw.ElapsedMilliseconds,Is.GreaterThan(200),"Uri was returned before failtimeout has passed");

        }

        [Test]
        public void IfThereIsNoUriToTestOrValidUriPoolReturnsInvalidUrisOrderedByLastAttemptStartTest()
        {
            var uris = Enumerable.Range(1, 10).Select(i => new Uri("http://localhost:" + (1000 + i))).ToArray();
            var pool = new UriPool(200000, 100000, uris);
            Uri uri = null;
            
            //fail whole pool
            foreach (var u in pool.Take(10)) 
            {
                if(uri==null)
                    uri = u.Uri;
                pool.ReportAttempt(u.Uri,false); 
                Thread.Sleep(10);
            }
            Assert.That(pool.First().Uri, Is.EqualTo(uri), "Inavlid uri that was used maximum tiem ago was not returned");
            Console.WriteLine(uri);


            var dict = new Dictionary<PoolUri, int>();
            foreach (var u in pool.Take(100000))
            {
                if (!dict.ContainsKey(u))
                    dict[u] = 0;
                dict[u]++;
            }
            foreach (var pair in dict)
            {
                Console.WriteLine("{0} :\t{1}", pair.Key, pair.Value);
            }

            var mean = dict.Values.Sum() / dict.Count;
            decimal avgAbsoluteDeviation = (decimal)dict.Values.Average(v => Math.Abs(v - mean));
            decimal maxAbsoluteDeviation = dict.Values.Max(v => Math.Abs(v - mean));
            decimal minAbsoluteDeviation = dict.Values.Min(v => Math.Abs(v - mean));

            Console.WriteLine();
            Console.WriteLine("Average absolute deviation:\t{0}\t({1:P})", avgAbsoluteDeviation, avgAbsoluteDeviation / mean);
            Console.WriteLine("Maximum absolute deviation:\t{0}\t({1:P})", maxAbsoluteDeviation, maxAbsoluteDeviation / mean);
            Console.WriteLine("Minimum absolute deviation:\t{0}\t({1:P})", minAbsoluteDeviation, minAbsoluteDeviation / mean);


            Assert.That(dict.Values.Max(v => Math.Abs(v - mean)), Is.LessThan(mean * .05), "Maximum absolute deviation is more then 5%");
            Assert.That(avgAbsoluteDeviation, Is.LessThan(mean * .03), "Mean absolute deviation is more then 3%");

        }

        [Test]
        public void IfThereIsUriToBeTestedItWillBeSelectedButOnlyOnceTest()
        {
            var uris = Enumerable.Range(1, 10).Select(i =>new Uri("http://localhost:" + (1000 + i))).ToArray();
            var pool = new UriPool(200, 100000,uris);
            var uri = new Uri("http://localhost:1001/");
            foreach (var u in uris)
            {
                pool.ReportAttempt(u, u != uri); 
            }
            Thread.Sleep(250);
            Assert.That(pool.First().Uri,Is.EqualTo(uri),"Inavlid uri that has been tested more then failedtimout ms ago was not returned first");
            Assert.That(pool.Take(1000).Select(u => u.Uri),Is.All.Not.EqualTo(uri),"Uri beig tested was returned before first attempt to use it finished");
        }

        [Test]
        public void UriDistributionIsRegularTest()
        {
            var pool = new UriPool(60000, 100000, Enumerable.Range(1, 10).Select(i => new Uri("http://localhost:" + (1000 + i))).ToArray());
            var dict = new Dictionary<PoolUri, int> ();
         
            foreach (var uri in pool.Take(100000))
            {
                if (!dict.ContainsKey(uri))
                    dict[uri] = 0;
                dict[uri]++;
                pool.ReportAttempt(uri.Uri,true);
            }
            foreach (var pair in dict)
            {
                Console.WriteLine("{0} :\t{1}", pair.Key, pair.Value);
            }
            var mean = dict.Values.Sum() / dict.Count;
            decimal avgAbsoluteDeviation = (decimal) dict.Values.Average(v => Math.Abs(v - mean));
            decimal maxAbsoluteDeviation = dict.Values.Max(v => Math.Abs(v - mean));
            decimal minAbsoluteDeviation = dict.Values.Min(v => Math.Abs(v - mean));

            Console.WriteLine();
            Console.WriteLine("Average absolute deviation:\t{0}\t({1:P})", avgAbsoluteDeviation, avgAbsoluteDeviation/mean);
            Console.WriteLine("Maximum absolute deviation:\t{0}\t({1:P})", maxAbsoluteDeviation, maxAbsoluteDeviation / mean);
            Console.WriteLine("Minimum absolute deviation:\t{0}\t({1:P})", minAbsoluteDeviation, minAbsoluteDeviation / mean);


            Assert.That(dict.Values.Max(v => Math.Abs(v - mean)), Is.LessThan(mean*.05), "Maximum absolute deviation is more then 5%");
            Assert.That(avgAbsoluteDeviation, Is.LessThan(mean *.03), "Mean absolute deviation is more then 3%");

        }
    }
}