using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class HostPrintEngineTests
    {
        private sealed class RecordingEngine : IPrintEngine
        {
            public readonly List<string> Jobs = new List<string>();
            public Exception Throw;

            public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
            {
                Jobs.Add(request.JobId);
                if (Throw != null) throw Throw;
                return PrintOutcome.Printed("ok");
            }
        }

        private static PrintRequest Request(string format)
        {
            return new PrintRequest { JobId = Guid.NewGuid().ToString(), PrinterName = "P", FriendlyName = "P", Format = format, FilePath = "x" };
        }

        [TestMethod]
        public void XpsGoesToTheXpsPrintApi()
        {
            var xps = new RecordingEngine();
            var system = new RecordingEngine();

            PrintOutcome outcome = new HostPrintEngine(xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(JobStates.Printed, outcome.State);
            Assert.AreEqual(1, xps.Jobs.Count);
            Assert.AreEqual(0, system.Jobs.Count);
        }

        [TestMethod]
        public void OpenXpsGoesToSystemPrinting()
        {
            var xps = new RecordingEngine();
            var system = new RecordingEngine();

            new HostPrintEngine(xps, system).Print(Request(JobFormats.Oxps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(0, xps.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void XpsPrintApiRefusal_FallsBackToSystemPrinting()
        {
            var xps = new RecordingEngine { Throw = new XpsPrintStartException("no api", null) };
            var system = new RecordingEngine();

            PrintOutcome outcome = new HostPrintEngine(xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(JobStates.Printed, outcome.State);
            Assert.AreEqual(1, xps.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void OtherFailures_AreNotRetriedOnTheSecondEngine()
        {
            var xps = new RecordingEngine { Throw = new InvalidOperationException("spooler said no") };
            var system = new RecordingEngine();

            Assert.ThrowsException<InvalidOperationException>(
                () => new HostPrintEngine(xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None));
            Assert.AreEqual(0, system.Jobs.Count, "a job that reached the spooler must not be printed twice");
        }
    }
}
