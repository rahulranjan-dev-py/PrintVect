using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Discovery;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class LocalNetworksTests
    {
        [TestMethod]
        public void BroadcastAndSubnetArithmetic()
        {
            Assert.AreEqual("192.168.10.255", LocalNetworks.BroadcastOf(IPAddress.Parse("192.168.10.218"), IPAddress.Parse("255.255.255.0")).ToString());
            Assert.AreEqual("172.16.191.255", LocalNetworks.BroadcastOf(IPAddress.Parse("172.16.183.66"), IPAddress.Parse("255.255.240.0")).ToString());
            Assert.IsTrue(LocalNetworks.SameSubnet(IPAddress.Parse("192.168.10.218"), IPAddress.Parse("192.168.10.220"), IPAddress.Parse("255.255.255.0")));
            Assert.IsFalse(LocalNetworks.SameSubnet(IPAddress.Parse("192.168.10.218"), IPAddress.Parse("172.16.183.66"), IPAddress.Parse("255.255.255.0")));
            Assert.IsFalse(LocalNetworks.SameSubnet(IPAddress.IPv6Loopback, IPAddress.Loopback, IPAddress.Parse("255.0.0.0")));
        }

        [TestMethod]
        public void PicksTheLocalAddressOnTheAskersSubnet()
        {
            var networks = new List<LocalNetwork>
            {
                new LocalNetwork { Address = IPAddress.Parse("172.16.183.66"), Mask = IPAddress.Parse("255.255.240.0"), InterfaceName = "Ethernet 2" },
                new LocalNetwork { Address = IPAddress.Parse("192.168.10.218"), Mask = IPAddress.Parse("255.255.255.0"), InterfaceName = "Ethernet" }
            };
            Assert.AreEqual("192.168.10.218", LocalNetworks.BestLocalAddressFor(IPAddress.Parse("192.168.10.220"), networks).ToString());
            Assert.AreEqual("172.16.183.66", LocalNetworks.BestLocalAddressFor(IPAddress.Parse("172.16.180.1"), networks).ToString());
            Assert.AreEqual("172.16.183.66", LocalNetworks.BestLocalAddressFor(IPAddress.Parse("192.168.1.9"), networks).ToString(), "no match: the first card");
            Assert.AreEqual("127.0.0.1", LocalNetworks.BestLocalAddressFor(IPAddress.Loopback, networks).ToString());
            Assert.IsNull(LocalNetworks.BestLocalAddressFor(IPAddress.Loopback, new List<LocalNetwork>()));
        }

        [TestMethod]
        public void ListingTheRealCardsDoesNotThrow()
        {
            IList<LocalNetwork> networks = LocalNetworks.List();
            foreach (LocalNetwork network in networks)
            {
                Assert.IsNotNull(network.Address);
                Assert.IsNotNull(network.Broadcast);
                Assert.IsTrue(network.Contains(network.Address));
            }
        }

        /// <summary>
        /// On Windows this calls the Network List Manager through the interface layouts in NetworkProfiles: a wrong method
        /// order there would fail here (a crash or a nonsense category), not on the office PCs. Elsewhere the list is empty.
        /// </summary>
        [TestMethod]
        public void ListingTheNetworkCategoriesDoesNotThrow()
        {
            IList<NetworkConnection> connected = NetworkProfiles.Connected();
            if (!WindowsInfo.IsWindows)
            {
                Assert.AreEqual(0, connected.Count);
                return;
            }
            // The build machine is online: an empty list here would mean the calls into Windows went wrong.
            Assert.IsTrue(connected.Count > 0, "Windows reported no connected network with an IPv4 address");
            foreach (NetworkConnection connection in connected)
            {
                Assert.IsFalse(string.IsNullOrEmpty(connection.Adapter));
                Assert.IsFalse(string.IsNullOrEmpty(connection.Network), connection.Adapter);
                Assert.AreNotEqual(Guid.Empty, connection.NetworkId, connection.Adapter);
                Assert.IsTrue(connection.Category == NetworkCategory.Public || connection.Category == NetworkCategory.Private
                              || connection.Category == NetworkCategory.Domain, connection.Adapter + ": " + connection.Category);
                Assert.IsTrue(connection.Addresses.Count > 0, connection.Adapter);
            }
            Assert.IsTrue(NetworkProfiles.Describe(connected).Any());
        }
    }

    [TestClass]
    public class DiscoveryMessagesTests
    {
        [TestMethod]
        public void RecognisesTheRequestText()
        {
            int version;
            Assert.IsTrue(DiscoveryMessages.IsRequest(Encoding.ASCII.GetBytes("PVECT-DISCOVER 1"), out version));
            Assert.AreEqual(1, version);
            Assert.IsTrue(DiscoveryMessages.IsRequest(Encoding.ASCII.GetBytes("PVECT-DISCOVER 2\r\n"), out version));
            Assert.AreEqual(2, version);
            Assert.IsFalse(DiscoveryMessages.IsRequest(Encoding.ASCII.GetBytes("MSETU-DISCOVER 1"), out version));
            Assert.IsFalse(DiscoveryMessages.IsRequest(Encoding.ASCII.GetBytes("PVECT-DISCOVER x"), out version));
            Assert.IsFalse(DiscoveryMessages.IsRequest(new byte[0], out version));
            Assert.IsFalse(DiscoveryMessages.IsRequest(null, out version));
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(ProtocolConstants.DiscoveryRequest), DiscoveryMessages.RequestBytes);
        }

        [TestMethod]
        public void ReplyRoundTripAndRejection()
        {
            var reply = new ListReply { Host = "COUNTER1", Ip = "192.168.10.218", Port = 9151 };
            reply.Printers.Add(new PrinterInfo { Id = "0afbc594d5975d71", Name = "HP Laser", Friendly = "Counter 1 Laser", Status = PrinterStatuses.Ready });
            ListReply parsed = DiscoveryMessages.ParseReply(DiscoveryMessages.EncodeReply(reply));
            Assert.IsNotNull(parsed);
            Assert.AreEqual("COUNTER1", parsed.Host);
            Assert.AreEqual(9151, parsed.Port);
            Assert.AreEqual(1, parsed.Printers.Count);
            Assert.AreEqual("Counter 1 Laser", parsed.Printers[0].Friendly);

            Assert.IsNull(DiscoveryMessages.ParseReply(Encoding.UTF8.GetBytes("not json")));
            Assert.IsNull(DiscoveryMessages.ParseReply(Encoding.UTF8.GetBytes("{\"app\":\"Other\",\"host\":\"X\"}")));
            Assert.IsNull(DiscoveryMessages.ParseReply(null));
        }
    }

    [TestClass]
    public class DiscoveryEndToEndTests
    {
        private static ListReply ReplyFor(IPAddress asker)
        {
            var reply = new ListReply { Host = "TESTHOST", Ip = asker.ToString(), Port = 9151 };
            reply.Printers.Add(new PrinterInfo { Id = "abc", Name = "Fake", Friendly = "Counter 1", Status = PrinterStatuses.Ready });
            return reply;
        }

        [TestMethod]
        public async Task AResponderAnswersAProbeOverLoopback()
        {
            using (var responder = new DiscoveryResponder(ReplyFor))
            using (var client = new DiscoveryClient(0))
            {
                responder.Start(0);
                Assert.IsTrue(responder.IsListening);
                Assert.IsTrue(responder.Port > 0);

                int changes = 0;
                client.HostsChanged += (s, e) => Interlocked.Increment(ref changes);
                ListReply reply = await client.ProbeAsync(new IPEndPoint(IPAddress.Loopback, responder.Port), TimeSpan.FromSeconds(5));

                Assert.IsNotNull(reply, "no answer from the responder");
                Assert.AreEqual("TESTHOST", reply.Host);
                Assert.AreEqual("127.0.0.1", reply.Ip, "the responder answers as the address on the asker's side");
                Assert.AreEqual(1, responder.RequestsAnswered);

                IList<DiscoveredHost> hosts = client.Hosts;
                Assert.AreEqual(1, hosts.Count);
                Assert.AreEqual("TESTHOST", hosts[0].Host);
                Assert.AreEqual("127.0.0.1", hosts[0].Ip);
                Assert.AreEqual(9151, hosts[0].Port);
                Assert.AreEqual("Counter 1", hosts[0].Printers[0].Friendly);
                Assert.IsFalse(hosts[0].Manual);
                Assert.AreEqual(1, changes);

                responder.Stop();
                Assert.IsFalse(responder.IsListening);
                ListReply none = await client.ProbeAsync(new IPEndPoint(IPAddress.Loopback, responder.Port), TimeSpan.FromMilliseconds(300));
                Assert.IsNull(none, "a stopped responder gives no answer");
            }
        }

        [TestMethod]
        public async Task ASearchRunsWhileThePageIsClosedAndStopsAfterwards()
        {
            using (var client = new DiscoveryClient(0))
            {
                Assert.IsFalse(client.IsRunning);
                await client.SearchAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
                Assert.IsFalse(client.IsRunning, "nobody wants discovery after the search: it stops");
                Assert.IsNotNull(client.LastRoundAt, "the search asked at least once");

                client.Start();   // the Office printers page opens
                await client.SearchAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                Assert.IsTrue(client.IsRunning, "a search does not stop the page's discovery");
                client.Stop();
                Assert.IsFalse(client.IsRunning);
            }
        }

        [TestMethod]
        public void AHostWithTwoNetworkCardsKeepsOneAddressWhileItAnswers()
        {
            using (var client = new DiscoveryClient(0) { Interval = TimeSpan.FromMilliseconds(100), Expiry = TimeSpan.FromSeconds(5) })
            {
                var reply = new ListReply { Host = "TWOCARDS", Ip = "ignored", Port = 9151 };
                int changes = 0;
                client.HostsChanged += (s, e) => Interlocked.Increment(ref changes);

                client.Record(reply, "192.168.10.218", "192.168.10.220");
                client.Record(reply, "172.27.122.6", "172.27.122.9");
                client.Record(reply, "192.168.10.218", "192.168.10.220");
                client.Record(reply, "172.27.122.6", "172.27.122.9");

                DiscoveredHost host = client.Hosts[0];
                Assert.AreEqual("192.168.10.218", host.Ip, "the first address stays while it keeps answering");
                Assert.AreEqual(2, host.Addresses.Count);
                Assert.AreEqual(1, changes, "the second card is not a change");

                Thread.Sleep(300);   // longer than one round plus a fifth: the first address has gone quiet
                client.Record(reply, "172.27.122.6", "172.27.122.9");
                Assert.AreEqual("172.27.122.6", client.Hosts[0].Ip, "the host moved to the address that still answers");
                Assert.AreEqual(2, changes);
            }
        }

        [TestMethod]
        public void ManualHostsStayAndDiscoveredOnesExpire()
        {
            using (var client = new DiscoveryClient(0) { Expiry = TimeSpan.FromMilliseconds(50) })
            {
                var typed = new ListReply { Host = "SPMPC", Ip = "10.0.0.5", Port = 9151 };
                typed.Printers.Add(new PrinterInfo { Id = "p1", Name = "Inkjet", Friendly = "COUNTER2", Status = PrinterStatuses.Ready });
                client.AddManual(typed, "10.0.0.5");
                Assert.AreEqual(1, client.Hosts.Count);
                Assert.IsTrue(client.Hosts[0].Manual);
                Assert.AreEqual("10.0.0.5", client.Hosts[0].Ip);

                client.AddManual(new ListReply { Host = "", Ip = "10.0.0.9", Port = 9151 }, "counter2");
                Assert.AreEqual(2, client.Hosts.Count, "a host without a name is listed under what was typed");
                Assert.AreEqual("counter2", client.Hosts[0].Host);
                Assert.AreEqual("10.0.0.9", client.Hosts[0].Ip, "a typed name keeps the address the host reported");

                Thread.Sleep(100);
                client.Cycle();   // not started: no network traffic, only the expiry pass
                Assert.AreEqual(2, client.Hosts.Count, "typed hosts never expire");

                client.Forget("SPMPC");
                Assert.AreEqual(1, client.Hosts.Count);
                Assert.IsFalse(client.IsRunning);
            }
        }
    }
}
