using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    internal sealed class FakeJobSender : IJobSender
    {
        public readonly List<RequestHeader> Sent = new List<RequestHeader>();
        public readonly List<string> StatusAsked = new List<string>();
        /// <summary>Returns the host's answer, or null to behave like an unreachable host.</summary>
        public Func<RequestHeader, JobReply> OnSend = h => new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printed, Message = "Printed on Fake" };
        public Func<string, JobReply> OnStatus = id => new JobReply { Ok = true, JobId = id, State = JobStates.Printed, Message = "Printed on Fake (status)" };

        public Task<JobReply> SendAsync(RemotePrinter printer, RequestHeader header, string filePath, CancellationToken ct)
        {
            lock (Sent) Sent.Add(header);
            Assert.IsTrue(File.Exists(filePath), "the file must still be there while it is sent");
            JobReply reply = OnSend(header);
            if (reply == null) throw new HostUnreachableException(printer.HostName + " is switched off");
            return Task.FromResult(reply);
        }

        public Task<JobReply> StatusAsync(RemotePrinter printer, string jobId, CancellationToken ct)
        {
            lock (StatusAsked) StatusAsked.Add(jobId);
            JobReply reply = OnStatus(jobId);
            if (reply == null) throw new HostUnreachableException(printer.HostName + " is switched off");
            return Task.FromResult(reply);
        }
    }

    internal sealed class FakeLocalQueue : ILocalPrintQueue
    {
        public readonly List<LocalQueueJob> Current = new List<LocalQueueJob>();
        public readonly List<string> Asked = new List<string>();

        public IList<LocalQueueJob> Jobs(string printerName)
        {
            lock (Asked) Asked.Add(printerName);
            lock (Current) return Current.Select(j => new LocalQueueJob { JobId = j.JobId, Document = j.Document, User = j.User }).ToList();
        }
    }

    [TestClass]
    public class SpoolWatcherTests
    {
        [TestMethod]
        public void TakesACompleteFileRenamesItAndReportsItsFormat()
        {
            using (var temp = new TempFolder())
            using (var watcher = new SpoolWatcher(temp.Path, "Test") { StableFor = TimeSpan.FromMilliseconds(100) })
            {
                SpoolFileReadyEventArgs ready = null;
                var seen = new ManualResetEventSlim();
                watcher.FileReady += (s, e) => { ready = e; seen.Set(); };
                watcher.Start();

                XpsPackages.Write(Path.Combine(temp.Path, "job.xps"), true, "From Notepad");
                for (int i = 0; i < 100 && !seen.IsSet; i++)
                {
                    watcher.Scan("test");
                    Thread.Sleep(50);
                }

                Assert.IsTrue(seen.IsSet, "the file was never taken");
                Assert.AreEqual(JobFormats.Oxps, ready.Format);
                Assert.AreEqual("From Notepad", ready.Title);
                Assert.IsTrue(Path.GetFileName(ready.FilePath).StartsWith("job-"), ready.FilePath);
                Assert.IsTrue(ready.FilePath.EndsWith(".oxps"), ready.FilePath);
                Assert.IsTrue(File.Exists(ready.FilePath));
                Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "job.xps")));
                Assert.IsTrue(ready.Size > 0);
            }
        }

        [TestMethod]
        public void NamesTheJobAfterTheDocumentInThePrinterQueue()
        {
            using (var temp = new TempFolder())
            {
                var queue = new FakeLocalQueue();
                queue.Current.Add(new LocalQueueJob { JobId = 7, Document = "Untitled - Notepad", User = "HP" });
                using (var watcher = new SpoolWatcher(temp.Path, "Test", "PrintVect - Test @HOST", queue) { StableFor = TimeSpan.FromMilliseconds(100) })
                {
                    SpoolFileReadyEventArgs ready = null;
                    var seen = new ManualResetEventSlim();
                    watcher.FileReady += (s, e) => { ready = e; seen.Set(); };
                    watcher.Start();

                    XpsPackages.Write(Path.Combine(temp.Path, "job.xps"), true, null);
                    for (int i = 0; i < 100 && !seen.IsSet; i++)
                    {
                        watcher.Scan("test");
                        Thread.Sleep(50);
                    }

                    Assert.IsTrue(seen.IsSet, "the file was never taken");
                    Assert.AreEqual("Untitled - Notepad", ready.Document);
                    Assert.AreEqual("HP", ready.User);
                    Assert.IsNull(ready.Title);
                    CollectionAssert.Contains(queue.Asked, "PrintVect - Test @HOST");

                    // The queue entry is used once: a second file without a queue job falls back to the file's own title.
                    queue.Current.Clear();
                    seen.Reset();
                    XpsPackages.Write(Path.Combine(temp.Path, "job.xps"), true, "Inside title");
                    for (int i = 0; i < 100 && !seen.IsSet; i++)
                    {
                        watcher.Scan("test");
                        Thread.Sleep(50);
                    }
                    Assert.IsTrue(seen.IsSet);
                    Assert.AreEqual("Inside title", ready.Document);
                }
            }
        }

        [TestMethod]
        public void MovesFilesThatAreNotPackagesToFailed()
        {
            using (var temp = new TempFolder())
            using (var watcher = new SpoolWatcher(temp.Path, "Test") { StableFor = TimeSpan.FromMilliseconds(50) })
            {
                int readyCount = 0;
                watcher.FileReady += (s, e) => Interlocked.Increment(ref readyCount);
                watcher.Start();
                File.WriteAllText(Path.Combine(temp.Path, "job.xps"), "this is not a package");

                string failed = Path.Combine(temp.Path, ClientPrinterNames.FailedFolderName);
                for (int i = 0; i < 100 && (!Directory.Exists(failed) || Directory.GetFiles(failed).Length == 0); i++)
                {
                    watcher.Scan("test");
                    Thread.Sleep(50);
                }

                Assert.AreEqual(0, readyCount);
                Assert.AreEqual(1, Directory.GetFiles(failed).Length);
                Assert.IsFalse(File.Exists(Path.Combine(temp.Path, "job.xps")));
            }
        }

        [TestMethod]
        public void LeavesAFileAloneWhileItIsStillOpen()
        {
            if (!WindowsInfo.IsWindows)
            {
                Assert.Inconclusive("exclusive file sharing is only enforced by Windows");
            }
            using (var temp = new TempFolder())
            using (var watcher = new SpoolWatcher(temp.Path, "Test") { StableFor = TimeSpan.FromMilliseconds(50) })
            {
                int readyCount = 0;
                watcher.FileReady += (s, e) => Interlocked.Increment(ref readyCount);
                watcher.Start();
                string path = Path.Combine(temp.Path, "job.xps");
                XpsPackages.Write(path, false, null);
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        watcher.Scan("test");
                        Thread.Sleep(50);
                    }
                    Assert.AreEqual(0, readyCount, "a file the spooler still writes must not be taken");
                }
                for (int i = 0; i < 100 && readyCount == 0; i++)
                {
                    watcher.Scan("test");
                    Thread.Sleep(50);
                }
                Assert.AreEqual(1, readyCount);
            }
        }
    }

    [TestClass]
    public class ClientServiceTests
    {
        private static RemotePrinter Printer()
        {
            return new RemotePrinter
            {
                PrinterId = "0afbc594d5975d71",
                HostName = "COUNTER1",
                HostIp = "127.0.0.1",
                Port = 9151,
                LocalPrinterName = "PrintVect - Laser @COUNTER1",
                FriendlyName = "Laser"
            };
        }

        private static ClientService Service(TempFolder temp, FakeJobSender sender, params int[] retryDelaysMs)
        {
            var paths = new AppPaths(temp.Path);
            paths.EnsureDirectories();
            var service = new ClientService(paths, sender, new ClientJobTracker(), new FakeLocalQueue())
            {
                StableFor = TimeSpan.FromMilliseconds(50),
                StatusPollInterval = TimeSpan.FromMilliseconds(50),
                StatusPollMax = TimeSpan.FromSeconds(5),
                Pin = "1234"
            };
            if (retryDelaysMs.Length > 0)
            {
                service.RetryDelays = retryDelaysMs.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray();
            }
            else
            {
                service.RetryDelays = new[] { TimeSpan.Zero };
            }
            return service;
        }

        private static ClientJobRecord DropAndWait(ClientService service, AppPaths paths, RemotePrinter printer, bool openXps, string title, int timeoutSeconds = 10)
        {
            ClientJobRecord finished = null;
            var done = new ManualResetEventSlim();
            service.JobFinished += (s, r) => { finished = r; done.Set(); };
            XpsPackages.Write(ClientPrinterNames.PortFile(paths, printer.PrinterId), openXps, title);
            for (int i = 0; i < timeoutSeconds * 20 && !done.IsSet; i++)
            {
                service.ScanNow(printer.PrinterId);
                Thread.Sleep(50);
            }
            Assert.IsTrue(done.IsSet, "the job never finished");
            return finished;
        }

        [TestMethod]
        public void AJobIsSentWithTheRightHeaderAndKeptInSent()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, true, "Letter to RMS");

                    Assert.AreEqual(ClientJobStates.Printed, record.State);
                    Assert.AreEqual("Letter to RMS", record.Doc);
                    Assert.AreEqual(1, sender.Sent.Count);
                    RequestHeader header = sender.Sent[0];
                    Assert.AreEqual(printer.PrinterId, header.PrinterId);
                    Assert.AreEqual(JobFormats.Oxps, header.Format);
                    Assert.AreEqual(record.JobId, header.JobId);
                    Assert.AreEqual("Letter to RMS", header.Doc);
                    Assert.AreEqual(PinHash.Compute("1234"), header.Pin);
                    Assert.IsTrue(record.FilePath.Contains(Path.DirectorySeparatorChar + ClientPrinterNames.SentFolderName + Path.DirectorySeparatorChar), record.FilePath);
                    Assert.IsTrue(File.Exists(record.FilePath));
                    Assert.AreEqual(1, service.Jobs.CountToday(ClientJobStates.Printed));
                }
            }
        }

        [TestMethod]
        public void TheCopiesCountFromTheFileTravelsInTheHeader()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord finished = null;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { finished = r; done.Set(); };
                    XpsPackages.Write(ClientPrinterNames.PortFile(paths, printer.PrinterId), true, "Two copies", 0, 2);
                    for (int i = 0; i < 200 && !done.IsSet; i++)
                    {
                        service.ScanNow(printer.PrinterId);
                        Thread.Sleep(50);
                    }

                    Assert.IsTrue(done.IsSet, "the job never finished");
                    Assert.AreEqual(2, finished.Copies);
                    Assert.AreEqual(2, sender.Sent[0].Copies);
                }
            }
        }

        [TestMethod]
        public void ARefusedJobEndsInFailed()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender { OnSend = h => JobReply.Error(h.JobId, "No shared printer called Laser") };
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, null);

                    Assert.AreEqual(ClientJobStates.Error, record.State);
                    StringAssert.Contains(record.Message, "No shared printer called Laser");
                    Assert.IsTrue(record.FilePath.Contains(Path.DirectorySeparatorChar + ClientPrinterNames.FailedFolderName + Path.DirectorySeparatorChar), record.FilePath);
                }
            }
        }

        [TestMethod]
        public void AnUnreachableHostLeavesTheJobPendingAndRetryResendsIt()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender { OnSend = h => null };
                using (ClientService service = Service(temp, sender, 0, 20, 20))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Pending one");

                    Assert.AreEqual(ClientJobStates.Pending, record.State);
                    Assert.AreEqual(3, sender.Sent.Count, "three tries");
                    StringAssert.Contains(record.Message, "COUNTER1");
                    Assert.AreEqual(1, service.PendingCount(printer.PrinterId));

                    sender.OnSend = h => new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printed, Message = "Printed later" };
                    ClientJobRecord retried = null;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { retried = r; done.Set(); };
                    Assert.AreEqual(1, service.RetryPending(printer.PrinterId));
                    for (int i = 0; i < 200 && !done.IsSet; i++)
                    {
                        service.ScanNow(printer.PrinterId);
                        Thread.Sleep(50);
                    }
                    Assert.IsTrue(done.IsSet, "the retried job never finished");
                    Assert.AreEqual(ClientJobStates.Printed, retried.State);
                    Assert.AreEqual(record.JobId, retried.JobId, "Retry resends the same job, it does not create a new one");
                    Assert.AreEqual("Pending one", retried.Doc);
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                    Assert.AreEqual(record.JobId, sender.Sent.Last().JobId);
                }
            }
        }

        [TestMethod]
        public void PendingFilesFromAnEarlierRunKeepTheirIdOnRetry()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    string pending = Path.Combine(ClientPrinterNames.SpoolFolder(paths, printer.PrinterId), ClientPrinterNames.PendingFolderName);
                    string oldId = Guid.NewGuid().ToString("D");
                    XpsPackages.Write(Path.Combine(pending, "job-" + oldId + ".oxps"), true, "Left over");
                    service.StartWatching(printer);
                    Assert.AreEqual(1, service.PendingCount(printer.PrinterId));

                    ClientJobRecord finished = null;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { finished = r; done.Set(); };
                    Assert.AreEqual(1, service.RetryPending(printer.PrinterId));
                    Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)), "the job never finished");

                    Assert.AreEqual(oldId, finished.JobId);
                    Assert.AreEqual("Left over", finished.Doc);
                    Assert.AreEqual(JobFormats.Oxps, finished.Format);
                    Assert.AreEqual(ClientJobStates.Printed, finished.State);
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                    Assert.AreEqual("job-" + oldId + ".oxps", sender.Sent[0].FileName, "the file keeps its name and id");
                }
            }
        }

        [TestMethod]
        public void AHostThatIsStillPrintingIsAskedUntilItIsDone()
        {
            using (var temp = new TempFolder())
            {
                int asked = 0;
                var sender = new FakeJobSender
                {
                    OnSend = h => new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printing, Message = "Windows is sending the job" },
                    OnStatus = id => Interlocked.Increment(ref asked) < 3
                        ? new JobReply { Ok = true, JobId = id, State = JobStates.Printing, Message = "still printing" }
                        : new JobReply { Ok = true, JobId = id, State = JobStates.Printed, Message = "Printed on Laser (COUNTER1)" }
                };
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, null);

                    Assert.AreEqual(ClientJobStates.Printed, record.State);
                    Assert.AreEqual("Printed on Laser (COUNTER1)", record.Message);
                    Assert.IsTrue(asked >= 3, "status was asked " + asked + " times");
                }
            }
        }

        [TestMethod]
        public void SetPrintersStartsAndStopsWatchers()
        {
            using (var temp = new TempFolder())
            using (ClientService service = Service(temp, new FakeJobSender()))
            {
                RemotePrinter a = Printer();
                var b = new RemotePrinter { PrinterId = "bbbbbbbbbbbbbbbb", HostName = "SPM", HostIp = "10.0.0.2", FriendlyName = "Inkjet", LocalPrinterName = "PrintVect - Inkjet @SPM" };
                service.SetPrinters(new[] { a, b });
                CollectionAssert.AreEquivalent(new[] { a.PrinterId, b.PrinterId }, service.WatchedPrinterIds.ToArray());

                service.SetPrinters(new[] { b });
                CollectionAssert.AreEquivalent(new[] { b.PrinterId }, service.WatchedPrinterIds.ToArray());
                Assert.IsNull(service.FindPrinter(a.PrinterId));
                Assert.IsNotNull(service.FindPrinter(b.PrinterId));
            }
        }

        [TestMethod]
        public void SendFileCopiesIntoTheSpoolFolder()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);
                    string sample = XpsPackages.Write(temp.File("sample.xps"), false, "Test page");

                    ClientJobRecord finished = null;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { finished = r; done.Set(); };
                    service.SendFile(printer.PrinterId, sample);
                    for (int i = 0; i < 200 && !done.IsSet; i++)
                    {
                        service.ScanNow(printer.PrinterId);
                        Thread.Sleep(50);
                    }

                    Assert.IsTrue(done.IsSet);
                    Assert.AreEqual(ClientJobStates.Printed, finished.State);
                    Assert.IsTrue(File.Exists(sample), "the original stays where it was");
                    Assert.ThrowsException<InvalidOperationException>(() => service.SendFile("nope", sample));
                }
            }
        }
    }
}
