using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UTApp
{
    [TestClass]
    public class Program
    {
        static void Main(string[] args)
        {
            // Make sure to run the test as a part of the visual studio test runner. (File->Test->)
        }

        [TestMethod]
        public void TestWithFake()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            using (ShimsContext.Create())
            {
                //System.Threading.Fakes.Thread.sFakes.Thread.Sleep
                System.Threading.Fakes.ShimThread.SleepTimeSpan = (arg) =>
                {
                    return;
                };

                TestSleep(TimeSpan.FromSeconds(10));
            }

            // We expect the elapsed time to be much less than 10 seconds
            Console.WriteLine("Elasped time: " + sw.Elapsed);
        }

        [TestMethod]
        public void TestWithoutFake()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            TestSleep(TimeSpan.FromSeconds(10));

            // We expect the elapsed time to be 10 seconds
            Console.WriteLine("Elasped time: " + sw.Elapsed);
        }

        static void TestSleep(TimeSpan timeToSleep)
        {
            Thread.Sleep(timeToSleep);
        }
    }
}
