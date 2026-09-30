using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class JobTrackerTests
    {
        [TestMethod]
        public void AddUpdateAndCount()
        {
            var tracker = new JobTracker();
            var changes = new List<string>();
            tracker.Changed += (s, r) => changes.Add(r.State);

            tracker.Add(new JobRecord { JobId = "a", State = JobStates.Queued, ReceivedAt = DateTime.Now });
            tracker.Add(new JobRecord { JobId = "b", State = JobStates.Queued, ReceivedAt = DateTime.Now });
            JobRecord printed = tracker.Update("a", JobStates.Printed, "done");
            JobRecord failed = tracker.Update("b", JobStates.Error, "no paper");

            Assert.AreEqual("done", printed.Message);
            Assert.IsNotNull(printed.FinishedAt);
            Assert.AreEqual(1, tracker.CountToday(JobStates.Printed));
            Assert.AreEqual(1, tracker.CountToday(JobStates.Error));
            Assert.AreEqual(2, tracker.CountReceivedToday());
            Assert.AreEqual(4, changes.Count);
            Assert.IsNull(tracker.Update("zzz", JobStates.Printed, "?"));

            JobRecord snapshot;
            Assert.IsTrue(tracker.TryGet("A", out snapshot), "ids are matched case-insensitively");
            snapshot.Message = "changed the copy";
            tracker.TryGet("a", out snapshot);
            Assert.AreEqual("done", snapshot.Message, "callers get snapshots, not the live record");
            Assert.IsFalse(tracker.TryGet("nope", out snapshot));
            Assert.AreEqual("b", tracker.Recent(1)[0].JobId);
            Assert.AreEqual("no paper", failed.Message);
        }

        [TestMethod]
        public void OldRecordsAreTrimmed()
        {
            var tracker = new JobTracker();
            for (int i = 0; i < JobTracker.MaxRecords + 5; i++)
            {
                tracker.Add(new JobRecord { JobId = "job" + i, State = JobStates.Printed, ReceivedAt = DateTime.Now });
            }

            JobRecord snapshot;
            Assert.IsFalse(tracker.TryGet("job0", out snapshot));
            Assert.IsTrue(tracker.TryGet("job" + (JobTracker.MaxRecords + 4), out snapshot));
        }
    }

    [TestClass]
    public class SharedPrinterResolverTests
    {
        private static readonly SharedPrinter[] Printers =
        {
            new SharedPrinter { Id = "id-laser", LocalName = "HP LaserJet 1020", FriendlyName = "Counter 1 Laser" },
            new SharedPrinter { Id = "id-pdf", LocalName = "Microsoft Print to PDF", FriendlyName = "PDF" }
        };

        [TestMethod]
        public void FindsByIdFriendlyOrWindowsName()
        {
            Assert.AreEqual("id-laser", SharedPrinterResolver.Find(Printers, "id-laser").Id);
            Assert.AreEqual("id-laser", SharedPrinterResolver.Find(Printers, "counter 1 laser").Id);
            Assert.AreEqual("id-laser", SharedPrinterResolver.Find(Printers, " HP LASERJET 1020 ").Id);
            Assert.AreEqual("id-pdf", SharedPrinterResolver.Find(Printers, "pdf").Id);
            Assert.IsNull(SharedPrinterResolver.Find(Printers, "Canon"));
            Assert.IsNull(SharedPrinterResolver.Find(Printers, ""));
            Assert.IsNull(SharedPrinterResolver.Find(null, "x"));
        }

        [TestMethod]
        public void PrinterIds_AreStableAndDistinct()
        {
            string a = PrinterIds.For("COUNTER1", "HP LaserJet 1020");
            Assert.AreEqual(a, PrinterIds.For("counter1", "HP LaserJet 1020"));
            Assert.AreEqual(16, a.Length);
            Assert.AreNotEqual(a, PrinterIds.For("COUNTER1", "HP LaserJet 1021"));
            Assert.AreNotEqual(a, PrinterIds.For("COUNTER2", "HP LaserJet 1020"));
        }
    }

    [TestClass]
    public class HostServiceTests
    {
        private TempFolder _temp;
        private AppPaths _paths;
        private FakePrintEngine _engine;
        private HostService _host;

        [TestInitialize]
        public void Setup()
        {
            _temp = new TempFolder();
            _paths = new AppPaths(_temp.Path);
            _paths.EnsureDirectories();
            Log.Initialize(_paths.LogsDir, "PrintVect");
            _engine = new FakePrintEngine();
            _host = new HostService(_paths, _engine, new FakePrinters(), new JobTracker());
            _host.UpdateSharedPrinters(new[]
            {
                new SharedPrinter { Id = "abc123", LocalName = "Fake Printer", FriendlyName = "Counter 1 Laser" },
                new SharedPrinter { Id = "def456", LocalName = "Other Printer", FriendlyName = "Back Office" }
            });
            _host.Start(0);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _host.Stop();
            Log.Shutdown();
            _temp.Dispose();
        }

        private JobClient Client()
        {
            return new JobClient("127.0.0.1", _host.Port) { ConnectTimeout = TimeSpan.FromSeconds(5), TransferTimeout = TimeSpan.FromSeconds(20) };
        }

        private string WriteXps(int size)
        {
            var data = new byte[size];
            new Random(7).NextBytes(data);
            data[0] = (byte)'P';
            data[1] = (byte)'K';
            string path = _temp.File("test.xps");
            File.WriteAllBytes(path, data);
            return path;
        }

        [TestMethod]
        public async Task List_ReturnsSharedPrintersWithStatus()
        {
            ListReply reply = await Client().ListPrintersAsync(CancellationToken.None);

            Assert.IsTrue(reply.Ok);
            Assert.AreEqual("PrintVect", reply.App);
            Assert.AreEqual(Environment.MachineName, reply.Host);
            Assert.AreEqual(_host.Port, reply.Port);
            Assert.AreEqual("127.0.0.1", reply.Ip);
            Assert.AreEqual(2, reply.Printers.Count);
            Assert.AreEqual("abc123", reply.Printers[0].Id);
            Assert.AreEqual("Fake Printer", reply.Printers[0].Name);
            Assert.AreEqual("Counter 1 Laser", reply.Printers[0].Friendly);
            Assert.AreEqual("ready", reply.Printers[0].Status);
            Assert.AreEqual("offline", reply.Printers[1].Status);
        }

        [TestMethod]
        public async Task SendJob_PrintsThenDeletesTheFile()
        {
            string file = WriteXps(300000);
            var received = new List<JobRecord>();
            var finished = new List<JobRecord>();
            _host.JobReceived += (s, r) => { lock (received) received.Add(r); };
            _host.JobFinished += (s, r) => { lock (finished) finished.Add(r); };
            RequestHeader header = RequestHeader.ForJob("Counter 1 Laser", "test.xps", "xps", 0, "Test document", null);

            JobReply reply = await Client().SendJobAsync(header, file, null, CancellationToken.None);

            Assert.IsTrue(reply.Ok, reply.Message);
            Assert.AreEqual(JobStates.Printed, reply.State);
            Assert.AreEqual(header.JobId, reply.JobId);
            StringAssert.Contains(reply.Message, "Printed on Fake");
            Assert.AreEqual(1, _engine.Requests.Count);
            PrintRequest request = _engine.Requests[0];
            Assert.AreEqual("Fake Printer", request.PrinterName);
            Assert.AreEqual("Counter 1 Laser", request.FriendlyName);
            Assert.AreEqual("Test document", request.DocumentName);
            Assert.AreEqual(Environment.MachineName, request.ClientName);
            Assert.AreEqual("xps", request.Format);
            CollectionAssert.AreEqual(File.ReadAllBytes(file), _engine.LastFileBytes, "the host must print exactly what was sent");
            Assert.IsFalse(File.Exists(request.FilePath), "printed files are deleted");
            StringAssert.StartsWith(request.FilePath, _host.IncomingDirectory);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(1, finished.Count);
            Assert.AreEqual(JobStates.Printed, finished[0].State);
            Assert.AreEqual(1, _host.Jobs.CountToday(JobStates.Printed));

            JobReply status = await Client().QueryStatusAsync(header.JobId, CancellationToken.None);
            Assert.IsTrue(status.Ok);
            Assert.AreEqual(JobStates.Printed, status.State);
        }

        [TestMethod]
        public async Task SendJob_AcceptsIdAndWindowsName()
        {
            string file = WriteXps(2000);

            JobReply byId = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);
            JobReply byName = await Client().SendJobAsync(RequestHeader.ForJob("fake printer", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);

            Assert.IsTrue(byId.Ok, byId.Message);
            Assert.IsTrue(byName.Ok, byName.Message);
            Assert.AreEqual(2, _engine.Requests.Count);
        }

        [TestMethod]
        public async Task SendJob_WrongPin_IsRefusedWithReason()
        {
            _host.Pin = "1234";
            string file = WriteXps(50000);

            JobReply noPin = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);
            JobReply wrongPin = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", "9999"), file, null, CancellationToken.None);
            JobReply rightPin = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", "1234"), file, null, CancellationToken.None);

            Assert.IsFalse(noPin.Ok);
            StringAssert.Contains(noPin.Message, "PIN");
            Assert.IsFalse(wrongPin.Ok);
            Assert.IsTrue(rightPin.Ok, rightPin.Message);
            Assert.AreEqual(1, _engine.Requests.Count);
            Assert.AreEqual(0, Directory.GetFiles(_host.IncomingDirectory).Length, "refused jobs leave no file behind");
        }

        [TestMethod]
        public async Task SendJob_UnknownPrinter_ListsTheSharedOnes()
        {
            string file = WriteXps(2000);

            JobReply reply = await Client().SendJobAsync(RequestHeader.ForJob("Canon", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            Assert.AreEqual(JobStates.Error, reply.State);
            StringAssert.Contains(reply.Message, "No shared printer called \"Canon\"");
            StringAssert.Contains(reply.Message, "Counter 1 Laser");
            StringAssert.Contains(reply.Message, "Back Office");
        }

        [TestMethod]
        public async Task SendJob_BadFormat_IsRefused()
        {
            string file = WriteXps(2000);

            JobReply reply = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.pdf", "pdf", 0, "", null), file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            StringAssert.Contains(reply.Message, "not supported");
        }

        [TestMethod]
        public async Task SendJob_NotAnXpsFile_IsRefusedAndKept()
        {
            string file = _temp.File("junk.xps");
            File.WriteAllText(file, "this is not a zip file at all");

            JobReply reply = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "junk.xps", "xps", 0, "", null), file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            StringAssert.Contains(reply.Message, "not an XPS document");
            Assert.AreEqual(1, Directory.GetFiles(_host.IncomingDirectory).Length, "kept for diagnosis");
            Assert.AreEqual(0, _engine.Requests.Count);
        }

        [TestMethod]
        public async Task SendJob_PrinterFailure_IsReportedAndFileKept()
        {
            _engine.Outcome = PrintOutcome.Error("the printer reports paper out");
            string file = WriteXps(2000);

            JobReply reply = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            Assert.AreEqual(JobStates.Error, reply.State);
            StringAssert.Contains(reply.Message, "paper out");
            Assert.AreEqual(1, Directory.GetFiles(_host.IncomingDirectory).Length);
            Assert.AreEqual(1, _host.Jobs.CountToday(JobStates.Error));
        }

        [TestMethod]
        public async Task SendJob_ShortBody_IsLoggedAndHostKeepsWorking()
        {
            var header = RequestHeader.ForJob("abc123", "a.xps", "xps", 1000, "", null);
            using (var raw = new TcpClient())
            {
                await raw.ConnectAsync("127.0.0.1", _host.Port);
                using (NetworkStream stream = raw.GetStream())
                {
                    await Framing.WriteHeaderAsync(stream, header, CancellationToken.None);
                    await stream.WriteAsync(new byte[] { (byte)'P', (byte)'K', 3, 4, 5, 6, 7, 8, 9, 10 }, 0, 10);
                    raw.Client.Shutdown(SocketShutdown.Send);
                    // The host cannot answer a job that never fully arrived; it just closes.
                    var buffer = new byte[16];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    Assert.AreEqual(0, read);
                }
            }

            JobRecord record;
            Assert.IsTrue(_host.Jobs.TryGet(header.JobId, out record));
            Assert.AreEqual(JobStates.Error, record.State);
            StringAssert.Contains(record.Message, "incomplete");
            Assert.AreEqual(0, Directory.GetFiles(_host.IncomingDirectory).Length);

            ListReply list = await Client().ListPrintersAsync(CancellationToken.None);
            Assert.IsTrue(list.Ok, "the host must survive a broken connection");
        }

        [TestMethod]
        public async Task Status_UnknownJob_IsAnError()
        {
            JobReply reply = await Client().QueryStatusAsync(Guid.NewGuid().ToString(), CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            StringAssert.Contains(reply.Message, "no record");
        }

        [TestMethod]
        public async Task WrongMagic_GetsAnErrorReply()
        {
            using (var raw = new TcpClient())
            {
                await raw.ConnectAsync("127.0.0.1", _host.Port);
                using (NetworkStream stream = raw.GetStream())
                {
                    byte[] junk = System.Text.Encoding.ASCII.GetBytes("HELLO PRINTER\n");
                    await stream.WriteAsync(junk, 0, junk.Length);
                    string line = await Framing.ReadReplyLineAsync(stream, CancellationToken.None);
                    JobReply reply = Framing.ParseReply<JobReply>(line);

                    Assert.IsFalse(reply.Ok);
                    StringAssert.Contains(reply.Message, "PVCT");
                }
            }
        }

        [TestMethod]
        public async Task WrongProtocolVersion_IsRefused()
        {
            string file = WriteXps(2000);
            RequestHeader header = RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null);
            header.Version = 2;

            JobReply reply = await Client().SendJobAsync(header, file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            StringAssert.Contains(reply.Message, "version");
            Assert.AreEqual(0, _engine.Requests.Count);
        }

        [TestMethod]
        public async Task NoSharedPrinters_IsRefusedWithAHint()
        {
            _host.UpdateSharedPrinters(new SharedPrinter[0]);
            string file = WriteXps(2000);

            JobReply reply = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, null, CancellationToken.None);

            Assert.IsFalse(reply.Ok);
            StringAssert.Contains(reply.Message, "not sharing any printer");
        }

        [TestMethod]
        public async Task Stop_ClosesThePortAndClientGetsAClearMessage()
        {
            int port = _host.Port;
            _host.Stop();
            Assert.IsFalse(_host.IsListening);

            try
            {
                await new JobClient("127.0.0.1", port).ListPrintersAsync(CancellationToken.None);
                Assert.Fail("expected HostUnreachableException");
            }
            catch (HostUnreachableException ex)
            {
                StringAssert.Contains(ex.Message, "sharing ON");
            }
        }

        [TestMethod]
        public async Task StuckPrinter_DoesNotHoldUpAnotherPrinter()
        {
            _host.ReplyWait = TimeSpan.FromMilliseconds(500);
            ManualResetEventSlim gate = _engine.Block("Fake Printer");
            string file = WriteXps(2000);
            RequestHeader stuck = RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "stuck", null);

            JobReply stuckReply = await Client().SendJobAsync(stuck, file, null, CancellationToken.None);
            JobReply otherReply = await Client().SendJobAsync(RequestHeader.ForJob("def456", "b.xps", "xps", 0, "other", null), file, null, CancellationToken.None);

            Assert.IsTrue(stuckReply.Ok, stuckReply.Message);
            Assert.AreEqual(JobStates.Printing, stuckReply.State, "the reply after the wait says printing, not queued");
            Assert.IsTrue(otherReply.Ok, otherReply.Message);
            Assert.AreEqual(JobStates.Printed, otherReply.State, "the other printer must not wait behind the stuck one");
            Assert.AreEqual(1, _host.PendingPrintCount);

            gate.Set();
            await WaitUntilAsync(() => StateOf(stuck.JobId) == JobStates.Printed, TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, _host.PendingPrintCount);
        }

        [TestMethod]
        public async Task StuckJob_IsReportedAndLaterJobsForThatPrinterStillPrint()
        {
            _host.ReplyWait = TimeSpan.FromMilliseconds(300);
            _host.StuckTimeout = TimeSpan.FromMilliseconds(800);
            var finished = new List<JobRecord>();
            _host.JobFinished += (s, r) => { lock (finished) finished.Add(r); };
            ManualResetEventSlim gate = _engine.Block("Fake Printer");
            string file = WriteXps(2000);
            RequestHeader stuck = RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "stuck", null);

            JobReply stuckReply = await Client().SendJobAsync(stuck, file, null, CancellationToken.None);
            Assert.AreEqual(JobStates.Printing, stuckReply.State);

            await WaitUntilAsync(() => StateOf(stuck.JobId) == JobStates.Error, TimeSpan.FromSeconds(5));
            JobRecord record;
            _host.Jobs.TryGet(stuck.JobId, out record);
            StringAssert.Contains(record.Message, "has not finished");
            StringAssert.Contains(record.Message, "Counter 1 Laser");

            // The stuck thread still holds the first job, but the printer now has a fresh thread.
            lock (_engine.Blocked) { _engine.Blocked.Remove("Fake Printer"); }
            JobReply next = await Client().SendJobAsync(RequestHeader.ForJob("abc123", "b.xps", "xps", 0, "next", null), file, null, CancellationToken.None);
            Assert.IsTrue(next.Ok, next.Message);
            Assert.AreEqual(JobStates.Printed, next.State);
            Assert.AreEqual(2, _engine.Requests.Count);

            // When Windows finally lets go, the record is corrected.
            gate.Set();
            await WaitUntilAsync(() => StateOf(stuck.JobId) == JobStates.Printed, TimeSpan.FromSeconds(5));
            lock (finished)
            {
                Assert.IsTrue(finished.Any(r => r.JobId == stuck.JobId && r.State == JobStates.Error), "stuck report raised");
                Assert.IsTrue(finished.Any(r => r.JobId == stuck.JobId && r.State == JobStates.Printed), "late print raised");
            }
        }

        private string StateOf(string jobId)
        {
            JobRecord record;
            return _host.Jobs.TryGet(jobId, out record) ? record.State : null;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("condition not met within " + timeout);
                }
                await Task.Delay(50);
            }
        }

        [TestMethod]
        public async Task TwoJobs_ArePrintedInArrivalOrder()
        {
            _engine.Delay = TimeSpan.FromMilliseconds(300);
            string file = WriteXps(2000);
            RequestHeader first = RequestHeader.ForJob("abc123", "first.xps", "xps", 0, "first", null);
            RequestHeader second = RequestHeader.ForJob("abc123", "second.xps", "xps", 0, "second", null);

            Task<JobReply> a = Client().SendJobAsync(first, file, null, CancellationToken.None);
            await Task.Delay(100);
            Task<JobReply> b = Client().SendJobAsync(second, file, null, CancellationToken.None);
            JobReply[] replies = await Task.WhenAll(a, b);

            Assert.IsTrue(replies.All(r => r.Ok), replies[0].Message + " | " + replies[1].Message);
            Assert.AreEqual("first", _engine.Requests[0].DocumentName);
            Assert.AreEqual("second", _engine.Requests[1].DocumentName);
        }
    }
}
