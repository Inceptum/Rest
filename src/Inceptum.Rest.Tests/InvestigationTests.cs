using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;

namespace Inceptum.Rest.Tests
{
    [TestFixture]
    public class InvestigationTests
    {
        [Test, Ignore]
        public void RandomStrategyTest()
        {
            var dictionary1 = new Dictionary<int, int>();
            var dictionary2 = new Dictionary<int, int>();
            Stopwatch sw = Stopwatch.StartNew();
            var maxValue = 10;
            int[] sequence1 = Enumerable.Range(0, 10000).Select(i => BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 4) % maxValue).ToArray();
            Console.WriteLine("{0}ms ", sw.ElapsedMilliseconds);
            foreach (var i in sequence1)
            {
                if (!dictionary1.ContainsKey(i))
                    dictionary1[i] = 0;
                dictionary1[i]++;
            }
            foreach (KeyValuePair<int, int> pair in dictionary1.OrderBy(p => p.Key))
            {
                Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
            }

            sw = Stopwatch.StartNew();
            int[] sequence2 = Enumerable.Range(0, 10000).Select(i =>
            {
                var randomBytes = new byte[4];
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(randomBytes);

                    var seed = (randomBytes[0] & 0x7f) << 24 | randomBytes[1] << 16 | randomBytes[2] << 8 | randomBytes[3];
                    var random = new Random(seed);
                    return random.Next(0, maxValue);
                }
            }).ToArray();


            Console.WriteLine("{0}ms ", sw.ElapsedMilliseconds);
            foreach (var i in sequence2)
            {
                if (!dictionary2.ContainsKey(i))
                    dictionary2[i] = 0;
                dictionary2[i]++;

            }
            foreach (KeyValuePair<int, int> pair in dictionary2.OrderBy(p => p.Key))
            {
                Console.WriteLine("{0}:\t{1}", pair.Key, pair.Value);
            }

            /*

                        for (int i = 0; i < 10000; i++)
                        {
                            Console.WriteLine("{0}\t{1}", sequence1[i], sequence2[i]);
                        }*/
        }

        [Test, Ignore]
        public void StopwatchTest()
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                Thread.Sleep(1000);
                Console.WriteLine("{0}\t\t{1}", sw.ElapsedMilliseconds, DateTime.Now);
            }
        }
    }
}