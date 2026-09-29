using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Config;

namespace PrintVect.Tests
{
    [TestClass]
    public class ConfigStoreTests
    {
        [TestMethod]
        public void LoadOrCreate_WhenFileMissing_WritesDefaults()
        {
            using (var temp = new TempFolder())
            {
                var store = new ConfigStore(temp.File("config.json"));

                AppConfig config = store.LoadOrCreate();

                Assert.IsTrue(File.Exists(store.Path), "config.json should be created");
                Assert.AreEqual(AppConfig.DefaultDiscoveryPort, config.DiscoveryPort);
                Assert.AreEqual(AppConfig.DefaultJobPort, config.JobPort);
                Assert.AreEqual("", config.Pin);
                Assert.IsFalse(config.SharingEnabled);
                StringAssert.Contains(File.ReadAllText(store.Path), "\"JobPort\": 9151");
            }
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            using (var temp = new TempFolder())
            {
                var store = new ConfigStore(temp.File("config.json"));
                var original = new AppConfig
                {
                    DiscoveryPort = 9250,
                    JobPort = 9251,
                    Pin = "4321",
                    SharingEnabled = true,
                    StartWithWindows = false,
                    KeepSentFilesHours = 3,
                    Language = "hi",
                    SharedPrinters = new List<SharedPrinter>
                    {
                        new SharedPrinter { Id = "p1", LocalName = "HP LaserJet 1020", FriendlyName = "Counter 1 Laser" }
                    },
                    RemotePrinters = new List<RemotePrinter>
                    {
                        new RemotePrinter
                        {
                            PrinterId = "p1", HostName = "COUNTER1", HostIp = "192.168.1.5", Port = 9251,
                            LocalPrinterName = "PrintVect - Counter 1 Laser @COUNTER1", FriendlyName = "Counter 1 Laser"
                        }
                    }
                };

                store.Save(original);
                AppConfig loaded = store.Load();

                Assert.AreEqual(9250, loaded.DiscoveryPort);
                Assert.AreEqual(9251, loaded.JobPort);
                Assert.AreEqual("4321", loaded.Pin);
                Assert.IsTrue(loaded.SharingEnabled);
                Assert.IsFalse(loaded.StartWithWindows);
                Assert.AreEqual(3, loaded.KeepSentFilesHours);
                Assert.AreEqual("hi", loaded.Language);
                Assert.AreEqual(1, loaded.SharedPrinters.Count);
                Assert.AreEqual("Counter 1 Laser", loaded.SharedPrinters[0].FriendlyName);
                Assert.AreEqual(1, loaded.RemotePrinters.Count);
                Assert.AreEqual("COUNTER1", loaded.RemotePrinters[0].HostName);
                Assert.AreEqual("PrintVect - Counter 1 Laser @COUNTER1", loaded.RemotePrinters[0].LocalPrinterName);
                Assert.IsFalse(File.Exists(store.Path + ".tmp"), "temporary file must be renamed away");
            }
        }

        [TestMethod]
        public void Save_OverwritesExistingFile()
        {
            using (var temp = new TempFolder())
            {
                var store = new ConfigStore(temp.File("config.json"));
                store.Save(new AppConfig { JobPort = 9151 });

                store.Save(new AppConfig { JobPort = 9351 });

                Assert.AreEqual(9351, store.Load().JobPort);
            }
        }

        [TestMethod]
        public void Load_CorruptFile_ReturnsDefaultsAndKeepsACopy()
        {
            using (var temp = new TempFolder())
            {
                var store = new ConfigStore(temp.File("config.json"));
                File.WriteAllText(store.Path, "{ this is not json");

                AppConfig config = store.Load();

                Assert.AreEqual(AppConfig.DefaultJobPort, config.JobPort);
                string[] backups = Directory.GetFiles(temp.Path, "config.json.bad-*");
                Assert.AreEqual(1, backups.Length, "the unreadable file must be kept for the owner to look at");
            }
        }

        [TestMethod]
        public void Load_UnknownFieldsAndMissingFields_AreTolerated()
        {
            using (var temp = new TempFolder())
            {
                var store = new ConfigStore(temp.File("config.json"));
                File.WriteAllText(store.Path, "{ \"Version\": 1, \"JobPort\": 9200, \"FutureSetting\": true }");

                AppConfig config = store.Load();

                Assert.AreEqual(9200, config.JobPort);
                Assert.AreEqual(AppConfig.DefaultDiscoveryPort, config.DiscoveryPort);
                Assert.IsNotNull(config.SharedPrinters);
                Assert.IsNotNull(config.RemotePrinters);
                Assert.AreEqual("", config.Pin);
            }
        }

        [TestMethod]
        public void Normalize_ReservedOrInvalidPorts_FallBackToDefaults()
        {
            foreach (int bad in new[] { PortRules.RawRelayPort, PortRules.IppPort, 0, 80, -5, 70000 })
            {
                var config = new AppConfig { DiscoveryPort = bad, JobPort = bad };

                IList<string> warnings = ConfigStore.Normalize(config);

                Assert.AreEqual(AppConfig.DefaultDiscoveryPort, config.DiscoveryPort, "discovery port " + bad);
                Assert.AreEqual(AppConfig.DefaultJobPort, config.JobPort, "job port " + bad);
                Assert.IsTrue(warnings.Count >= 2, "two warnings expected for " + bad);
            }
        }

        [TestMethod]
        public void Normalize_SamePortTwice_ResetsBoth()
        {
            var config = new AppConfig { DiscoveryPort = 9300, JobPort = 9300 };

            IList<string> warnings = ConfigStore.Normalize(config);

            Assert.AreEqual(AppConfig.DefaultDiscoveryPort, config.DiscoveryPort);
            Assert.AreEqual(AppConfig.DefaultJobPort, config.JobPort);
            Assert.AreEqual(1, warnings.Count);
        }

        [TestMethod]
        public void Normalize_ValidCustomPorts_AreKept()
        {
            var config = new AppConfig { DiscoveryPort = 9250, JobPort = 9251 };

            IList<string> warnings = ConfigStore.Normalize(config);

            Assert.AreEqual(9250, config.DiscoveryPort);
            Assert.AreEqual(9251, config.JobPort);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void Normalize_RepairsNullsAndRanges()
        {
            var config = new AppConfig
            {
                Pin = null, Language = " ", KeepSentFilesHours = 0, SharedPrinters = null, RemotePrinters = null, Version = 99
            };

            IList<string> warnings = ConfigStore.Normalize(config);

            Assert.AreEqual("", config.Pin);
            Assert.AreEqual("en", config.Language);
            Assert.AreEqual(AppConfig.DefaultKeepSentFilesHours, config.KeepSentFilesHours);
            Assert.IsNotNull(config.SharedPrinters);
            Assert.IsNotNull(config.RemotePrinters);
            Assert.AreEqual(AppConfig.CurrentVersion, config.Version);
            Assert.IsTrue(warnings.Count >= 2);
        }

        [TestMethod]
        public void PortRules_KnowTheForbiddenPorts()
        {
            Assert.IsTrue(PortRules.IsAllowed(9150));
            Assert.IsTrue(PortRules.IsAllowed(9151));
            Assert.IsTrue(PortRules.IsAllowed(65535));
            Assert.IsFalse(PortRules.IsAllowed(9100));
            Assert.IsFalse(PortRules.IsAllowed(631));
            Assert.IsFalse(PortRules.IsAllowed(1023));
            Assert.IsFalse(PortRules.IsAllowed(65536));
        }
    }
}
