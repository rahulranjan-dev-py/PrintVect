using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Logging;

namespace PrintVect.Tests
{
    [TestClass]
    public class LogTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Log.Shutdown();
        }

        [TestMethod]
        public void FileNameFor_UsesPrefixAndIsoDate()
        {
            Assert.AreEqual("PrintVect-2026-09-29.log", Log.FileNameFor("PrintVect", new DateTime(2026, 9, 29, 13, 45, 0)));
            Assert.AreEqual("PrintVect-Elevate-2026-01-05.log", Log.FileNameFor("PrintVect-Elevate", new DateTime(2026, 1, 5)));
        }

        [TestMethod]
        public void TryParseFileName_RoundTrips()
        {
            string prefix;
            DateTime day;

            Assert.IsTrue(Log.TryParseFileName("PrintVect-Elevate-2026-09-29.log", out prefix, out day));
            Assert.AreEqual("PrintVect-Elevate", prefix);
            Assert.AreEqual(new DateTime(2026, 9, 29), day);

            Assert.IsFalse(Log.TryParseFileName("notes.txt", out prefix, out day));
            Assert.IsFalse(Log.TryParseFileName("PrintVect-2026-13-45.log", out prefix, out day), "impossible date");
            Assert.IsFalse(Log.TryParseFileName("", out prefix, out day));
        }

        [TestMethod]
        public void CleanupOldFiles_DeletesOnlyFilesOlderThanRetention()
        {
            using (var temp = new TempFolder())
            {
                var today = new DateTime(2026, 9, 29);
                string keepToday = temp.File(Log.FileNameFor("PrintVect", today));
                string keepEdge = temp.File(Log.FileNameFor("PrintVect", today.AddDays(-13)));
                string deleteEdge = temp.File(Log.FileNameFor("PrintVect", today.AddDays(-14)));
                string deleteOld = temp.File(Log.FileNameFor("PrintVect-Elevate", today.AddDays(-40)));
                string unrelated = temp.File("something-else.log");
                foreach (string f in new[] { keepToday, keepEdge, deleteEdge, deleteOld, unrelated })
                {
                    File.WriteAllText(f, "x");
                }

                int deleted = Log.CleanupOldFiles(temp.Path, 14, today);

                Assert.AreEqual(2, deleted);
                Assert.IsTrue(File.Exists(keepToday));
                Assert.IsTrue(File.Exists(keepEdge));
                Assert.IsFalse(File.Exists(deleteEdge));
                Assert.IsFalse(File.Exists(deleteOld));
                Assert.IsTrue(File.Exists(unrelated), "files that are not ours are never touched");
            }
        }

        [TestMethod]
        public void Write_AppendsLevelJobIdAndException()
        {
            using (var temp = new TempFolder())
            {
                Log.Initialize(temp.Path, "PrintVect");
                Assert.IsTrue(Log.IsInitialized);

                Log.Info("plain message");
                Log.Warn("job-42", "host not reachable");
                Log.Error("job-42", "send failed", new InvalidOperationException("boom"));
                Log.Shutdown();

                string file = Path.Combine(temp.Path, Log.FileNameFor("PrintVect", DateTime.Now));
                Assert.IsTrue(File.Exists(file), "today's file should exist");
                string text = File.ReadAllText(file);
                StringAssert.Contains(text, " INFO  [t");
                StringAssert.Contains(text, "plain message");
                StringAssert.Contains(text, " WARN  [t");
                StringAssert.Contains(text, "[job job-42] host not reachable");
                StringAssert.Contains(text, " ERROR [t");
                StringAssert.Contains(text, "[job job-42] send failed");
                StringAssert.Contains(text, "InvalidOperationException: boom");
            }
        }

        [TestMethod]
        public void Write_BeforeInitialize_DoesNotThrow()
        {
            Log.Shutdown();
            Log.Info("nobody is listening");
            Log.Error("still fine", new Exception("x"));
        }

        [TestMethod]
        public void TailLines_ReadsBackAcrossDaysWhileFileIsOpen()
        {
            using (var temp = new TempFolder())
            {
                DateTime today = DateTime.Now;
                File.WriteAllLines(temp.File(Log.FileNameFor("PrintVect", today.AddDays(-2))), new[] { "old-1", "old-2", "old-3" });
                File.WriteAllLines(temp.File(Log.FileNameFor("PrintVect", today.AddDays(-1))), new[] { "yesterday-1", "yesterday-2" });
                File.WriteAllLines(temp.File(Log.FileNameFor("PrintVect-Elevate", today)), new[] { "elevate-1" });

                Log.Initialize(temp.Path, "PrintVect");
                Log.Info("today-1");
                Log.Info("today-2");

                IList<string> tail = Log.TailLines(5);

                Assert.AreEqual(5, tail.Count);
                Assert.AreEqual("old-3", tail[0]);
                Assert.AreEqual("yesterday-1", tail[1]);
                Assert.AreEqual("yesterday-2", tail[2]);
                StringAssert.Contains(tail[3], "today-1");
                StringAssert.Contains(tail[4], "today-2");
                Assert.IsFalse(tail.Any(l => l.Contains("elevate-1")), "other programs' files are not mixed in");

                IList<string> shortTail = Log.TailLines(1);
                Assert.AreEqual(1, shortTail.Count);
                StringAssert.Contains(shortTail[0], "today-2");

                IList<string> all = Log.TailLines(100);
                Assert.AreEqual(7, all.Count);
                StringAssert.Contains(all[6], "today-2");
            }
        }

        [TestMethod]
        public void Initialize_DeletesOldFilesAndReportsIt()
        {
            using (var temp = new TempFolder())
            {
                string old = temp.File(Log.FileNameFor("PrintVect", DateTime.Today.AddDays(-30)));
                File.WriteAllText(old, "ancient");

                Log.Initialize(temp.Path, "PrintVect", 14);

                Assert.IsFalse(File.Exists(old));
                IList<string> tail = Log.TailLines(10);
                Assert.IsTrue(tail.Any(l => l.Contains("Deleted 1 log file(s)")), string.Join("|", tail));
            }
        }
    }
}
