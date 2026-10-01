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
        public void XpsGoesToTheSpoolerFirst()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();

            PrintOutcome outcome = new HostPrintEngine(spooler, xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(JobStates.Printed, outcome.State);
            Assert.AreEqual(1, spooler.Jobs.Count);
            Assert.AreEqual(0, xps.Jobs.Count);
            Assert.AreEqual(0, system.Jobs.Count);
        }

        [TestMethod]
        public void OpenXpsGoesToSystemPrinting()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();

            new HostPrintEngine(spooler, xps, system).Print(Request(JobFormats.Oxps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(0, spooler.Jobs.Count);
            Assert.AreEqual(0, xps.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void SpoolerRefusal_FallsBackToXpsPrintApi_ThenSystemPrinting()
        {
            var spooler = new RecordingEngine { Throw = new SpoolerStartException("no printer", null) };
            var xps = new RecordingEngine { Throw = new XpsPrintStartException("no api", null) };
            var system = new RecordingEngine();

            PrintOutcome outcome = new HostPrintEngine(spooler, xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(JobStates.Printed, outcome.State);
            Assert.AreEqual(1, spooler.Jobs.Count);
            Assert.AreEqual(1, xps.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void PromptingPort_GoesToSystemPrinting()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();
            var engine = new HostPrintEngine(spooler, xps, system, (printer, jobId) => "PORTPROMPT:", false);

            PrintOutcome outcome = engine.Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(JobStates.Printed, outcome.State);
            Assert.AreEqual(0, spooler.Jobs.Count, "a printer that asks for a file name must not use the direct spooler path");
            Assert.AreEqual(0, xps.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void UnknownPort_GoesToTheSpooler()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();
            var engine = new HostPrintEngine(spooler, xps, system, (printer, jobId) => null, false);

            engine.Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(1, spooler.Jobs.Count);
            Assert.AreEqual(0, system.Jobs.Count);
        }

        [TestMethod]
        public void PaperPort_GoesToTheSpooler()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();
            var engine = new HostPrintEngine(spooler, xps, system, (printer, jobId) => "USB001", false);

            engine.Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(1, spooler.Jobs.Count);
            Assert.AreEqual(0, system.Jobs.Count);
        }

        [TestMethod]
        public void OpenXps_GoesToTheSpoolerWhereWindowsConvertsIt()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();
            var engine = new HostPrintEngine(spooler, xps, system, (printer, jobId) => "USB001", true);

            engine.Print(Request(JobFormats.Oxps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(1, spooler.Jobs.Count, "Windows 8+ converts OpenXPS in the spooler");
            Assert.AreEqual(0, system.Jobs.Count);
        }

        [TestMethod]
        public void OpenXps_OnAPromptingPort_StillGoesToSystemPrinting()
        {
            var spooler = new RecordingEngine();
            var xps = new RecordingEngine();
            var system = new RecordingEngine();
            var engine = new HostPrintEngine(spooler, xps, system, (printer, jobId) => "PORTPROMPT:", true);

            engine.Print(Request(JobFormats.Oxps), (s, m) => { }, CancellationToken.None);

            Assert.AreEqual(0, spooler.Jobs.Count);
            Assert.AreEqual(1, system.Jobs.Count);
        }

        [TestMethod]
        public void IsPromptingPort_KnowsTheFilePorts()
        {
            Assert.IsTrue(PrinterPorts.IsPromptingPort("PORTPROMPT:"));
            Assert.IsTrue(PrinterPorts.IsPromptingPort("portprompt:"));
            Assert.IsTrue(PrinterPorts.IsPromptingPort("FILE:"));
            Assert.IsFalse(PrinterPorts.IsPromptingPort("USB001"));
            Assert.IsFalse(PrinterPorts.IsPromptingPort("192.168.1.20"));
            Assert.IsFalse(PrinterPorts.IsPromptingPort(""));
            Assert.IsFalse(PrinterPorts.IsPromptingPort(null));
        }

        [TestMethod]
        public void OtherFailures_AreNotRetriedOnAnotherEngine()
        {
            var spooler = new RecordingEngine { Throw = new InvalidOperationException("spooler said no") };
            var xps = new RecordingEngine();
            var system = new RecordingEngine();

            Assert.ThrowsException<InvalidOperationException>(
                () => new HostPrintEngine(spooler, xps, system).Print(Request(JobFormats.Xps), (s, m) => { }, CancellationToken.None));
            Assert.AreEqual(0, xps.Jobs.Count, "a job that reached the spooler must not be printed twice");
            Assert.AreEqual(0, system.Jobs.Count);
        }
    }
}
