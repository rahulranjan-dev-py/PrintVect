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

        public readonly List<RequestHeader> Hellos = new List<RequestHeader>();

        public Task<JobReply> HelloAsync(RemotePrinter printer, RequestHeader header, CancellationToken ct)
        {
            lock (Hellos) Hellos.Add(header);
            return Task.FromResult(new JobReply { Ok = true, Message = "hello" });
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
        public void TakesAFinishedFileWellBeforeTheNextPoll()
        {
            using (var temp = new TempFolder())
            using (var watcher = new SpoolWatcher(temp.Path, "Test"))
            {
                var seen = new ManualResetEventSlim();
                watcher.FileReady += (s, e) => seen.Set();
                watcher.Start();

                XpsPackages.Write(Path.Combine(temp.Path, "job.xps"), true, null);
                watcher.Scan("test");   // the event of the spooler's last write; after it nothing changes until the poll

                Assert.IsTrue(seen.Wait(TimeSpan.FromSeconds(1.5)), "the file waited for the " + SpoolWatcher.PollInterval.TotalSeconds + " s poll");
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
        public void AHostThatStopsAnsweringMidJobLeavesTheJobForRetry()
        {
            using (var temp = new TempFolder())
            {
                bool hostUp = true;
                var sender = new FakeJobSender
                {
                    OnSend = h => hostUp ? new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printing, Message = "Windows is sending the job" } : null,
                    OnStatus = id => null   // switched off right after it took the job
                };
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Mid job");

                    Assert.AreEqual(ClientJobStates.Pending, record.State);
                    StringAssert.Contains(record.Message, "COUNTER1 stopped answering");
                    StringAssert.Contains(record.Message, "Retry waiting");
                    Assert.AreEqual(1, service.PendingCount(printer.PrinterId), "the file went from sent\\ back to pending\\");

                    // The host is back: Retry sends the same job again and it prints.
                    sender.OnStatus = id => new JobReply { Ok = true, JobId = id, State = JobStates.Printed, Message = "Printed on Laser (COUNTER1)" };
                    ClientJobRecord retried = null;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { retried = r; done.Set(); };
                    Assert.AreEqual(1, service.RetryPending(printer.PrinterId));
                    Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)), "the retried job never finished");
                    Assert.AreEqual(ClientJobStates.Printed, retried.State);
                    Assert.AreEqual(record.JobId, retried.JobId, "Retry keeps the job id");
                    Assert.AreEqual(2, sender.Sent.Count);
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                }
            }
        }

        [TestMethod]
        public void AJobTheHostLostGoesBackToPending()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender
                {
                    OnSend = h => new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Queued, Message = "Received on COUNTER1" },
                    OnStatus = id => new JobReply { Ok = false, JobId = id, State = JobStates.Error, Retry = true,
                                                    Message = "PrintVect on COUNTER1 was closed before this job finished." }
                };
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Lost");

                    Assert.AreEqual(ClientJobStates.Pending, record.State);
                    StringAssert.Contains(record.Message, "was closed before this job finished");
                    Assert.AreEqual(1, service.PendingCount(printer.PrinterId));
                }
            }
        }

        [TestMethod]
        public void APrintingErrorIsNotOfferedForRetry()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender
                {
                    OnSend = h => new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printing, Message = "printing" },
                    OnStatus = id => new JobReply { Ok = false, JobId = id, State = JobStates.Error, Message = "The printer is out of paper." }
                };
                using (ClientService service = Service(temp, sender))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Paper");

                    Assert.AreEqual(ClientJobStates.Error, record.State);
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                }
            }
        }

        [TestMethod]
        public async Task PresenceSaysHelloOncePerHostWithItsPrinters()
        {
            var sender = new FakeJobSender();
            RemotePrinter a = Printer();
            var b = new RemotePrinter { PrinterId = "bbbbbbbbbbbbbbbb", HostName = "COUNTER1", HostIp = "127.0.0.1", FriendlyName = "Inkjet" };
            var c = new RemotePrinter { PrinterId = "cccccccccccccccc", HostName = "COUNTER2", HostIp = "10.0.0.2", FriendlyName = "Laser" };
            using (var announcer = new PresenceAnnouncer(sender, () => new List<RemotePrinter> { a, b, c }))
            {
                await announcer.AnnounceAsync(CancellationToken.None);
            }

            Assert.AreEqual(2, sender.Hellos.Count, "one hello per host");
            RequestHeader counter1 = sender.Hellos.Single(h => h.Printers.Contains(a.PrinterId));
            CollectionAssert.AreEquivalent(new[] { a.PrinterId, b.PrinterId }, counter1.Printers);
            Assert.AreEqual(RequestTypes.Hello, counter1.Type);
            Assert.AreEqual(Environment.MachineName, counter1.Client);
            Assert.IsFalse(string.IsNullOrEmpty(counter1.Os));
            Assert.IsFalse(string.IsNullOrEmpty(counter1.AppVersion));
        }

        [TestMethod]
        public void TheCopiesAnswerIsSentToTheHost()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    ClientJobRecord asked = null;
                    service.AskCopies = r => { asked = r; return Task.FromResult<int?>(3); };
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Three times");

                    Assert.IsNotNull(asked, "a job printed from a program is asked about");
                    Assert.AreEqual("Three times", asked.Doc);
                    Assert.AreEqual(ClientJobStates.Printed, record.State);
                    Assert.AreEqual(3, sender.Sent.Single().Copies);
                }
            }
        }

        [TestMethod]
        public void CancelInTheCopiesQuestionSendsNothing()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    service.AskCopies = r => Task.FromResult<int?>(null);
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, false, "Not wanted");

                    Assert.AreEqual(ClientJobStates.Cancelled, record.State);
                    Assert.AreEqual(0, sender.Sent.Count);
                    Assert.IsFalse(File.Exists(record.FilePath), "the cancelled file is deleted");
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                }
            }
        }

        [TestMethod]
        public void ATestPageIsNotAskedAboutCopies()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                using (ClientService service = Service(temp, sender))
                {
                    bool asked = false;
                    service.AskCopies = r => { asked = true; return Task.FromResult<int?>(5); };
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);
                    string sample = XpsPackages.Write(temp.File("PrintVect-test-page.xps"), false, "Test page");

                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => done.Set();
                    service.SendFile(printer.PrinterId, sample);
                    for (int i = 0; i < 200 && !done.IsSet; i++)
                    {
                        service.ScanNow(printer.PrinterId);
                        Thread.Sleep(50);
                    }

                    Assert.IsTrue(done.IsSet);
                    Assert.IsFalse(asked);
                    Assert.AreEqual(1, sender.Sent.Single().Copies);
                }
            }
        }

        [TestMethod]
        public void AWaitingJobCanBeCancelledOrRetriedOnItsOwn()
        {
            using (var temp = new TempFolder())
            {
                bool hostUp = false;
                var sender = new FakeJobSender
                {
                    OnSend = h => hostUp ? new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printed, Message = "Printed" } : null
                };
                using (ClientService service = Service(temp, sender, 0, 10))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord first = DropAndWait(service, paths, printer, false, "First");
                    ClientJobRecord second = DropAndWait(service, paths, printer, false, "Second");
                    Assert.AreEqual(ClientJobStates.Pending, first.State);
                    Assert.AreEqual(ClientJobStates.Pending, second.State);
                    Assert.AreEqual(2, service.PendingCount(printer.PrinterId));
                    int sentBefore = sender.Sent.Count;

                    Assert.IsTrue(service.CancelWaiting(first.JobId));
                    Assert.AreEqual(ClientJobStates.Cancelled, service.Jobs.Find(first.JobId).State);
                    Assert.AreEqual(1, service.PendingCount(printer.PrinterId), "only the cancelled job's file is gone");
                    Assert.IsFalse(service.CancelWaiting(first.JobId), "already cancelled");

                    hostUp = true;
                    var done = new ManualResetEventSlim();
                    service.JobFinished += (s, r) => { if (r.JobId == second.JobId) done.Set(); };
                    Assert.IsTrue(service.RetryJob(second.JobId));
                    Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)));
                    Assert.AreEqual(ClientJobStates.Printed, service.Jobs.Find(second.JobId).State);
                    Assert.AreEqual(sentBefore + 1, sender.Sent.Count, "the cancelled job was never sent");
                    Assert.IsFalse(service.RetryJob(first.JobId), "a cancelled job is not waiting");
                }
            }
        }

        [TestMethod]
        public void CancelAllWaitingAndClearTheList()
        {
            using (var temp = new TempFolder())
            {
                bool hostUp = true;
                var sender = new FakeJobSender
                {
                    OnSend = h => hostUp ? new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printed, Message = "Printed" } : null
                };
                using (ClientService service = Service(temp, sender, 0, 10))
                {
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);
                    ClientJobRecord printed = DropAndWait(service, paths, printer, false, "Printed one");
                    hostUp = false;
                    DropAndWait(service, paths, printer, false, "Waiting one");
                    DropAndWait(service, paths, printer, false, "Waiting two");
                    var removed = new List<string>();
                    service.Jobs.Removed += (s, id) => removed.Add(id);

                    Assert.AreEqual(1, service.Jobs.RemoveDone(), "only the printed job leaves; waiting jobs stay");
                    CollectionAssert.AreEqual(new[] { printed.JobId }, removed);
                    Assert.IsNull(service.Jobs.Find(printed.JobId));

                    Assert.AreEqual(2, service.CancelAllWaiting());
                    Assert.AreEqual(0, service.PendingCount(printer.PrinterId));
                    Assert.IsTrue(service.Jobs.All().All(r => r.State == ClientJobStates.Cancelled));
                    Assert.AreEqual(2, service.Jobs.RemoveDone());
                    Assert.AreEqual(0, service.Jobs.All().Count);
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
                var b = new RemotePrinter { PrinterId = "bbbbbbbbbbbbbbbb", HostName = "COUNTER2", HostIp = "10.0.0.2", FriendlyName = "Inkjet", LocalPrinterName = "PrintVect - Inkjet @COUNTER2" };
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

        [TestMethod]
        public void AHostThatMovedIsFoundAgainAndTheJobPrintsThereAtOnce()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender();
                RemotePrinter printer = Printer();
                printer.HostIp = "10.0.0.1";
                // Only the new address answers, as after a phone hotspot handed the host another one.
                sender.OnSend = h => printer.HostIp == "10.0.0.2"
                    ? new JobReply { Ok = true, JobId = h.JobId, State = JobStates.Printed, Message = "Printed on COUNTER1" }
                    : null;
                int searches = 0;
                using (ClientService service = Service(temp, sender, 0, 5000, 5000))
                {
                    service.FindHost = (p, ct) =>
                    {
                        Interlocked.Increment(ref searches);
                        p.HostIp = "10.0.0.2";
                        return Task.FromResult(true);
                    };
                    var paths = new AppPaths(temp.Path);
                    service.StartWatching(printer);
                    DateTime start = DateTime.UtcNow;

                    ClientJobRecord record = DropAndWait(service, paths, printer, true, "Moved host");

                    Assert.AreEqual(ClientJobStates.Printed, record.State, record.Message);
                    Assert.AreEqual(1, searches);
                    Assert.AreEqual(2, sender.Sent.Count, "one try at the old address, one at the new");
                    Assert.IsTrue(DateTime.UtcNow - start < TimeSpan.FromSeconds(4), "the found host is tried at once, not after the 5 s wait");
                }
            }
        }

        [TestMethod]
        public void AHostThatIsNotFoundIsSearchedForOnceAndTheJobWaits()
        {
            using (var temp = new TempFolder())
            {
                var sender = new FakeJobSender { OnSend = h => null };
                int searches = 0;
                using (ClientService service = Service(temp, sender, 0, 10, 10))
                {
                    service.FindHost = (p, ct) =>
                    {
                        Interlocked.Increment(ref searches);
                        return Task.FromResult(false);
                    };
                    var paths = new AppPaths(temp.Path);
                    RemotePrinter printer = Printer();
                    service.StartWatching(printer);

                    ClientJobRecord record = DropAndWait(service, paths, printer, true, "Nobody home");

                    Assert.AreEqual(ClientJobStates.Pending, record.State);
                    Assert.AreEqual(1, searches);
                    Assert.AreEqual(3, sender.Sent.Count);
                    Assert.AreEqual("127.0.0.1", printer.HostIp, "the address stays when the host was not found elsewhere");
                }
            }
        }
    }
}
