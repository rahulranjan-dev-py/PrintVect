using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class HostIdentityTests
    {
        // Made once: an RSA key takes a moment.
        private static readonly HostIdentity _real = HostIdentity.Create();
        private static readonly HostIdentity _impostor = HostIdentity.Create();

        private static ListReply Answer(HostIdentity identity, string challenge, string host, string address)
        {
            return new ListReply { Host = host, Ip = address, Port = 9151, HostKey = identity.PublicKey, HostSig = identity.Sign(challenge, host, address) };
        }

        private static RemotePrinter Printer(string keyId)
        {
            return new RemotePrinter { PrinterId = "0afbc594d5975d71", HostName = "COUNTER1", HostIp = "192.168.10.20", HostKeyId = keyId };
        }

        [TestMethod]
        public void ASignatureHoldsOnlyForItsChallengeNameAndAddress()
        {
            string challenge = HostKeys.NewChallenge();
            string sig = _real.Sign(challenge, "COUNTER1", "192.168.10.31");

            Assert.IsTrue(HostKeys.Verify(_real.PublicKey, challenge, "counter1", "192.168.10.31", sig), "names compare without case");
            Assert.IsFalse(HostKeys.Verify(_real.PublicKey, HostKeys.NewChallenge(), "COUNTER1", "192.168.10.31", sig), "another challenge");
            Assert.IsFalse(HostKeys.Verify(_real.PublicKey, challenge, "COUNTER1", "192.168.10.32", sig), "another address: a relayed answer");
            Assert.IsFalse(HostKeys.Verify(_real.PublicKey, challenge, "COUNTER2", "192.168.10.31", sig), "another name");
            Assert.IsFalse(HostKeys.Verify(_impostor.PublicKey, challenge, "COUNTER1", "192.168.10.31", sig), "another key");
            Assert.IsFalse(HostKeys.Verify("not base64!", challenge, "COUNTER1", "192.168.10.31", sig));
            Assert.AreEqual(64, _real.KeyId.Length);
            Assert.AreNotEqual(_real.KeyId, _impostor.KeyId);
        }

        [TestMethod]
        public void APrinterFollowsOnlyTheHostWhoseKeyItRemembers()
        {
            string challenge = HostKeys.NewChallenge();
            string keyId;

            Assert.IsNull(MovedHostCheck.Judge(Printer(_real.KeyId), "192.168.10.31", challenge, Answer(_real, challenge, "COUNTER1", "192.168.10.31"), out keyId));
            Assert.AreEqual(_real.KeyId, keyId);

            string impostor = MovedHostCheck.Judge(Printer(_real.KeyId), "192.168.10.66", challenge, Answer(_impostor, challenge, "COUNTER1", "192.168.10.66"), out keyId);
            StringAssert.Contains(impostor, "not COUNTER1");

            ListReply claimsTheRealKey = Answer(_impostor, challenge, "COUNTER1", "192.168.10.66");
            claimsTheRealKey.HostKey = _real.PublicKey;   // copied from the real host's discovery reply, but it cannot sign with it
            Assert.IsNotNull(MovedHostCheck.Judge(Printer(_real.KeyId), "192.168.10.66", challenge, claimsTheRealKey, out keyId));

            ListReply relayed = Answer(_real, challenge, "COUNTER1", "192.168.10.31");   // the real host's answer, passed on from another address
            Assert.IsNotNull(MovedHostCheck.Judge(Printer(_real.KeyId), "192.168.10.66", challenge, relayed, out keyId));

            ListReply noKey = new ListReply { Host = "COUNTER1", Ip = "192.168.10.66", Port = 9151 };
            Assert.IsNotNull(MovedHostCheck.Judge(Printer(_real.KeyId), "192.168.10.66", challenge, noKey, out keyId), "a remembered key is never given up");
        }

        [TestMethod]
        public void APrinterWithoutAKeyLearnsTheFirstProvenOne_AndFollowsAnOlderHostAsBefore()
        {
            string challenge = HostKeys.NewChallenge();
            string keyId;

            Assert.IsNull(MovedHostCheck.Judge(Printer(null), "192.168.10.31", challenge, Answer(_real, challenge, "COUNTER1", "192.168.10.31"), out keyId));
            Assert.AreEqual(_real.KeyId, keyId);

            Assert.IsNull(MovedHostCheck.Judge(Printer(null), "192.168.10.31", challenge, new ListReply { Host = "COUNTER1", Port = 9151 }, out keyId),
                "a host before 0.5.8 has no key");
            Assert.IsNull(keyId);
        }

        [TestMethod]
        public async Task ARealHostProvesItselfOverTheNetwork()
        {
            using (var temp = new TempFolder())
            {
                var paths = new AppPaths(temp.Path);
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, "PrintVect");
                using (var host = new HostService(paths, new FakePrintEngine(), new FakePrinters(), new JobTracker()) { Identity = _real })
                {
                    host.Start(0);
                    var printer = new RemotePrinter { PrinterId = "abc123", HostName = Environment.MachineName, HostKeyId = _real.KeyId };

                    MovedHostVerdict proven = await MovedHostCheck.CheckAsync(printer, "127.0.0.1", host.Port, CancellationToken.None);
                    printer.HostKeyId = _impostor.KeyId;
                    MovedHostVerdict other = await MovedHostCheck.CheckAsync(printer, "127.0.0.1", host.Port, CancellationToken.None);

                    Assert.IsNull(proven.Problem, proven.Problem);
                    Assert.AreEqual(_real.KeyId, proven.KeyId);
                    Assert.IsNotNull(other.Problem);
                    host.Stop();
                }
                Log.Shutdown();
            }
        }

        [TestMethod]
        public async Task BeforeAJob_ThePcAtTheSavedAddressMustProveTheKey_AndAPassedCheckHoldsForAWhile()
        {
            using (var temp = new TempFolder())
            {
                var paths = new AppPaths(temp.Path);
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, "PrintVect");
                var engine = new FakePrintEngine();
                using (var host = new HostService(paths, engine, new FakePrinters(), new JobTracker()) { Identity = _real })
                {
                    host.UpdateSharedPrinters(new[] { new SharedPrinter { Id = "abc123", LocalName = "Fake Printer", FriendlyName = "Counter 1 Laser" } });
                    host.Start(0);
                    string file = XpsPackages.Write(temp.File("job.xps"), false, "Checked", 2000);
                    // No PC name: only the saved address is tried.
                    var printer = new RemotePrinter { PrinterId = "abc123", HostIp = "127.0.0.1", Port = host.Port, HostKeyId = _real.KeyId };
                    var sender = new TcpJobSender();

                    JobReply first = await sender.SendAsync(printer, RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, CancellationToken.None);
                    Assert.IsTrue(first.Ok, first.Message);

                    host.Identity = _impostor;   // another PC now answers at the saved address
                    JobReply cached = await sender.SendAsync(printer, RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, CancellationToken.None);
                    Assert.IsTrue(cached.Ok, "a passed check is trusted for 5 minutes");

                    sender.CheckPassedFor = TimeSpan.Zero;
                    HostUnreachableException refused = await Assert.ThrowsExceptionAsync<HostUnreachableException>(
                        () => sender.SendAsync(printer, RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null), file, CancellationToken.None));
                    StringAssert.Contains(refused.Message, "Nothing was sent there");
                    Assert.AreEqual(2, engine.Requests.Count, "the third job never reached the PC that could not prove the key");
                    host.Stop();
                }
                Log.Shutdown();
            }
        }

        [TestMethod]
        public async Task BeforeAJob_APrinterWithoutAKeyLearnsIt()
        {
            using (var temp = new TempFolder())
            {
                var paths = new AppPaths(temp.Path);
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, "PrintVect");
                using (var host = new HostService(paths, new FakePrintEngine(), new FakePrinters(), new JobTracker()) { Identity = _real })
                {
                    host.UpdateSharedPrinters(new[] { new SharedPrinter { Id = "abc123", LocalName = "Fake Printer", FriendlyName = "Counter 1 Laser" } });
                    host.Start(0);
                    var printer = new RemotePrinter { PrinterId = "abc123", HostIp = "127.0.0.1", Port = host.Port };
                    var sender = new TcpJobSender();
                    RemotePrinter learned = null;
                    sender.HostKeyLearned += (s, p) => learned = p;

                    JobReply reply = await sender.SendAsync(printer, RequestHeader.ForJob("abc123", "a.xps", "xps", 0, "", null),
                        XpsPackages.Write(temp.File("job.xps"), false, "Learn", 2000), CancellationToken.None);

                    Assert.IsTrue(reply.Ok, reply.Message);
                    Assert.AreEqual(_real.KeyId, printer.HostKeyId);
                    Assert.AreSame(printer, learned, "the app saves config.json");
                    host.Stop();
                }
                Log.Shutdown();
            }
        }

        [TestMethod]
        public void TheKeyIsKeptProtectedAndReadBack()
        {
            string saved;
            using (HostIdentity made = HostIdentity.LoadOrCreate("", out saved))
            {
                Assert.IsNotNull(saved, "a new key is handed back to be kept in config.json");
                string again;
                using (HostIdentity loaded = HostIdentity.LoadOrCreate(saved, out again))
                {
                    Assert.IsNull(again, "nothing new to keep");
                    Assert.AreEqual(made.KeyId, loaded.KeyId);
                }
            }
            Assert.IsNull(MachineProtection.Unprotect("bm90IHByb3RlY3RlZA=="), "text protected elsewhere (or not at all) is not a key");
        }
    }
}
