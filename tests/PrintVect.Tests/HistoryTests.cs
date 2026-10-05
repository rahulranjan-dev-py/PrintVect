using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class JobHistoryStoreTests
    {
        [TestMethod]
        public void SavesAndLoadsRecords()
        {
            using (var temp = new TempFolder())
            {
                string path = temp.File("jobs-host.json");
                var records = new List<JobRecord>
                {
                    new JobRecord { JobId = "a", Doc = "Letter", State = JobStates.Printed, ReceivedAt = new DateTime(2026, 10, 5, 9, 0, 0) },
                    new JobRecord { JobId = "b", Doc = "Form", State = JobStates.Error, Message = "paper out", ReceivedAt = new DateTime(2026, 10, 5, 9, 1, 0) }
                };
                using (var store = new JobHistoryStore<JobRecord>(path, () => records))
                {
                    Assert.AreEqual(0, store.Load().Count, "no file yet");
                    store.QueueSave();
                    store.Flush();
                    Assert.IsTrue(File.Exists(path));
                    Assert.IsFalse(File.Exists(path + ".tmp"));
                }

                using (var again = new JobHistoryStore<JobRecord>(path, () => new List<JobRecord>()))
                {
                    IList<JobRecord> loaded = again.Load();
                    Assert.AreEqual(2, loaded.Count);
                    Assert.AreEqual("Letter", loaded[0].Doc);
                    Assert.AreEqual("paper out", loaded[1].Message);
                    Assert.AreEqual(new DateTime(2026, 10, 5, 9, 1, 0), loaded[1].ReceivedAt);
                }
            }
        }

        [TestMethod]
        public void DisposeSavesPendingChanges()
        {
            using (var temp = new TempFolder())
            {
                string path = temp.File("jobs-client.json");
                var records = new List<ClientJobRecord> { new ClientJobRecord { JobId = "x", Doc = "Notepad", State = ClientJobStates.Printed, StartedAt = DateTime.Now } };
                var store = new JobHistoryStore<ClientJobRecord>(path, () => records);
                store.QueueSave();
                store.Dispose();
                Assert.IsTrue(File.Exists(path));
                using (var again = new JobHistoryStore<ClientJobRecord>(path, () => records))
                {
                    Assert.AreEqual("Notepad", again.Load()[0].Doc);
                }
            }
        }

        [TestMethod]
        public void ABadFileIsSetAsideAndGivesAnEmptyList()
        {
            using (var temp = new TempFolder())
            {
                string path = temp.File("jobs-host.json");
                File.WriteAllText(path, "{ this is not a list");
                using (var store = new JobHistoryStore<JobRecord>(path, () => new List<JobRecord>()))
                {
                    Assert.AreEqual(0, store.Load().Count);
                }
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual(1, Directory.GetFiles(temp.Path, "jobs-host.json.bad-*").Length);
            }
        }
    }

    [TestClass]
    public class HistoryRestoreTests
    {
        [TestMethod]
        public void HostTrackerRestoresAndMarksUnfinishedJobs()
        {
            var tracker = new JobTracker();
            int events = 0;
            tracker.Changed += (s, r) => events++;
            int restored = tracker.Restore(new[]
            {
                new JobRecord { JobId = "old", State = JobStates.Printed, ReceivedAt = DateTime.Today.AddHours(8), FinishedAt = DateTime.Today.AddHours(8) },
                new JobRecord { JobId = "cut", State = JobStates.Printing, ReceivedAt = DateTime.Today.AddHours(9) },
                new JobRecord { JobId = "cut", State = JobStates.Printing, ReceivedAt = DateTime.Today.AddHours(9) },
                null
            });

            Assert.AreEqual(2, restored);
            Assert.AreEqual(0, events, "restoring raises no events");
            JobRecord cut;
            Assert.IsTrue(tracker.TryGet("cut", out cut));
            Assert.AreEqual(JobStates.Error, cut.State);
            StringAssert.Contains(cut.Message, "closed");
            Assert.AreEqual(1, tracker.CountToday(JobStates.Printed));
            Assert.AreEqual(1, tracker.CountToday(JobStates.Error));
            Assert.AreEqual("cut", tracker.Recent(1)[0].JobId, "newest first");
            Assert.AreEqual(2, tracker.All().Count);

            tracker.Add(new JobRecord { JobId = "new", State = JobStates.Queued, ReceivedAt = DateTime.Now });
            Assert.AreEqual("new", tracker.Recent(1)[0].JobId);
        }

        [TestMethod]
        public void ClientTrackerRestoresAndMarksUnfinishedJobs()
        {
            var tracker = new ClientJobTracker();
            int restored = tracker.Restore(new[]
            {
                new ClientJobRecord { JobId = "done", State = ClientJobStates.Printed, StartedAt = DateTime.Today.AddHours(8) },
                new ClientJobRecord { JobId = "sending", State = ClientJobStates.Sending, StartedAt = DateTime.Today.AddHours(9), HostName = "COUNTER1" },
                new ClientJobRecord { JobId = "printing", State = ClientJobStates.Printing, StartedAt = DateTime.Today.AddHours(10), HostName = "COUNTER1" }
            });

            Assert.AreEqual(3, restored);
            Assert.AreEqual(ClientJobStates.Pending, tracker.Find("sending").State);
            Assert.AreEqual(ClientJobStates.Error, tracker.Find("printing").State);
            StringAssert.Contains(tracker.Find("printing").Message, "COUNTER1");
            Assert.AreEqual(1, tracker.CountToday(ClientJobStates.Printed));
            Assert.AreEqual("printing", tracker.Recent(1)[0].JobId, "newest first");
            Assert.AreEqual(0, tracker.Restore(new[] { new ClientJobRecord { JobId = "done", State = ClientJobStates.Printed } }), "no duplicates");
            Assert.AreEqual(3, tracker.All().Count);
        }
    }
}
