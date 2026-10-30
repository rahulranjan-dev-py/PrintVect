using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Config;
using PrintVect.Core.Host;

namespace PrintVect.Tests
{
    [TestClass]
    public class ConnectionLimiterTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void OnePcGetsAFewConnectionsAtOnce_OthersAreNotAffected()
        {
            var limiter = new ConnectionLimiter { MaxPerAddress = 2 };
            Assert.IsNull(limiter.TryEnter("192.168.10.23", Now));
            Assert.IsNull(limiter.TryEnter("192.168.10.23", Now));
            Assert.IsNotNull(limiter.TryEnter("192.168.10.23", Now));
            Assert.IsNull(limiter.TryEnter("192.168.10.24", Now));

            limiter.Leave("192.168.10.23");
            Assert.IsNull(limiter.TryEnter("192.168.10.23", Now), "a closed connection frees its place");
            Assert.AreEqual(3, limiter.OpenCount);
        }

        [TestMethod]
        public void TheHostServesAtMostSoManyInAll()
        {
            var limiter = new ConnectionLimiter { MaxTotal = 3 };
            for (int i = 1; i <= 3; i++) Assert.IsNull(limiter.TryEnter("192.168.10." + i, Now));
            StringAssert.Contains(limiter.TryEnter("192.168.10.9", Now), "3 connections");
        }

        [TestMethod]
        public void TooManyNewConnectionsInAMinute_AreRefusedUntilTheMinuteIsOver()
        {
            var limiter = new ConnectionLimiter { MaxPerMinute = 3 };
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNull(limiter.TryEnter("192.168.10.23", Now.AddSeconds(i)));
                limiter.Leave("192.168.10.23");
            }
            Assert.IsNotNull(limiter.TryEnter("192.168.10.23", Now.AddSeconds(10)));
            Assert.IsNull(limiter.TryEnter("192.168.10.23", Now.AddSeconds(61)));
        }
    }

    [TestClass]
    public class NonceMemoryTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void ANonceIsAcceptedOnceUntilItsProofExpires()
        {
            var memory = new NonceMemory();
            Assert.IsTrue(memory.TryRemember("n1", Now.AddHours(24), Now));
            Assert.IsTrue(memory.WasUsed("n1", Now.AddHours(1)));
            Assert.IsFalse(memory.TryRemember("n1", Now.AddHours(24), Now.AddHours(23)));
            Assert.IsTrue(memory.TryRemember("n2", Now.AddHours(24), Now));
            Assert.IsFalse(memory.WasUsed("n1", Now.AddHours(25)), "after its proof expired it no longer needs keeping");
        }

        [TestMethod]
        public void AFullMemoryDropsTheSoonestToExpire()
        {
            var memory = new NonceMemory { MaxEntries = 3 };
            memory.TryRemember("soon", Now.AddHours(1), Now);
            memory.TryRemember("later", Now.AddHours(20), Now);
            memory.TryRemember("latest", Now.AddHours(23), Now);
            memory.TryRemember("new", Now.AddHours(24), Now);

            Assert.AreEqual(3, memory.Count);
            Assert.IsFalse(memory.WasUsed("soon", Now));
            Assert.IsTrue(memory.WasUsed("latest", Now));
            Assert.IsTrue(memory.WasUsed("new", Now));
        }
    }

    [TestClass]
    public class IncomingSpoolTests
    {
        [TestMethod]
        public void OldFilesNoJobWaitsFor_AreDeleted_OthersStay()
        {
            using (var temp = new TempFolder())
            {
                var spool = new IncomingSpool(new AppPaths(temp.Path));
                spool.Ensure();
                string oldUnused = Path.Combine(spool.Directory, "old-unused.xps");
                string oldWaiting = Path.Combine(spool.Directory, "old-waiting.xps");
                string recent = Path.Combine(spool.Directory, "recent.xps");
                foreach (string file in new[] { oldUnused, oldWaiting, recent }) File.WriteAllBytes(file, new byte[1000]);
                File.SetLastWriteTime(oldUnused, DateTime.Now.AddHours(-25));
                File.SetLastWriteTime(oldWaiting, DateTime.Now.AddHours(-25));

                int deleted = spool.DeleteUnusedOlderThan(TimeSpan.FromHours(24), new[] { Path.GetFullPath(oldWaiting) });

                Assert.AreEqual(1, deleted);
                Assert.IsFalse(File.Exists(oldUnused));
                Assert.IsTrue(File.Exists(oldWaiting), "a job still waits for it (a paused printer)");
                Assert.IsTrue(File.Exists(recent));
                Assert.AreEqual(2000, spool.TotalBytes());
            }
        }
    }
}
