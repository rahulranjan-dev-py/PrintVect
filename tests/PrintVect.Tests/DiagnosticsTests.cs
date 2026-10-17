using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;

namespace PrintVect.Tests
{
    [TestClass]
    public class DiagnosticsTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Log.Shutdown();
        }

        [TestMethod]
        public void Collect_ContainsEverySectionAndNeverThePin()
        {
            using (var temp = new TempFolder())
            {
                var paths = new AppPaths(temp.Path);
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, "PrintVect");
                Log.Info("hello from the test");
                var config = new AppConfig { Pin = "secret-pin-9876", JobPort = 9151 };
                bool extraCalled = false;

                string text = DiagnosticsReport.Collect(paths, config, r =>
                {
                    extraCalled = true;
                    r.AddSection("Printers on this PC", new[] { "- Test Printer (default)" });
                }, 50).ToString();

                Assert.IsTrue(extraCalled);
                foreach (string section in new[] { "== Program ==", "== Windows ==", "== Network ==", "== Settings ==",
                                                   "== Folders ==", "== Firewall rules ==", "== Printers on this PC ==",
                                                   "== Log files ==", "== Last 50 log lines ==" })
                {
                    StringAssert.Contains(text, section);
                }
                StringAssert.Contains(text, "PrintVect diagnostics, generated ");
                StringAssert.Contains(text, "PIN: set");
                Assert.IsFalse(text.Contains("secret-pin-9876"), "the PIN must never appear in diagnostics");
                StringAssert.Contains(text, "job port (TCP): 9151");
                StringAssert.Contains(text, "hello from the test");
                StringAssert.Contains(text, "- Test Printer (default)");
                StringAssert.Contains(text, "(ok, writable)");
                StringAssert.Contains(text, FirewallRules.JobsRuleName);
            }
        }

        [TestMethod]
        public void AddSection_EmptyBodySaysNothing()
        {
            string text = new DiagnosticsReport().AddSection("Empty", (string)null).ToString();

            StringAssert.Contains(text, "== Empty ==");
            StringAssert.Contains(text, "(nothing)");
        }

        [TestMethod]
        public void NetFrameworkInfo_MapsReleaseNumbers()
        {
            Assert.AreEqual("4.8", NetFrameworkInfo.NameForRelease(528040));
            Assert.AreEqual("4.8", NetFrameworkInfo.NameForRelease(528372));
            Assert.AreEqual("4.8.1", NetFrameworkInfo.NameForRelease(533325));
            Assert.AreEqual("4.7.2", NetFrameworkInfo.NameForRelease(461814));
            Assert.AreEqual("older than 4.5", NetFrameworkInfo.NameForRelease(1));
        }

        [TestMethod]
        public void ElevateResult_RoundTripsThroughJson()
        {
            var original = new ElevateResult
            {
                Command = "ping", Ok = true, ExitCode = ElevateExitCodes.Ok, Message = "fine",
                Timestamp = new DateTime(2026, 9, 29, 10, 30, 0)
            };

            ElevateResult copy = ElevateResult.FromJson(original.ToJson());

            Assert.AreEqual("ping", copy.Command);
            Assert.IsTrue(copy.Ok);
            Assert.AreEqual(0, copy.ExitCode);
            Assert.AreEqual("fine", copy.Message);
            Assert.AreEqual(original.Timestamp, copy.Timestamp);
        }

        [TestMethod]
        public void ElevateResult_DefaultFileLivesInSpool()
        {
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "PrintVectRoot"));

            string file = ElevateResult.DefaultFile(paths);

            Assert.AreEqual(Path.Combine(paths.SpoolDir, "elevate-result.json"), file);
        }

        [TestMethod]
        public void AppPaths_DeriveFromRoot()
        {
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "PrintVectRoot"));

            Assert.AreEqual(Path.Combine(paths.Root, "config.json"), paths.ConfigFile);
            Assert.AreEqual(Path.Combine(paths.Root, "spool"), paths.SpoolDir);
            Assert.AreEqual(Path.Combine(paths.Root, "logs"), paths.LogsDir);
        }
    }
}
