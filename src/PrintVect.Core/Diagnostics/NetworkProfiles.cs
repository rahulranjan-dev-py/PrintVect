using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    /// <summary>What Windows calls a network: Public (strangers' Wi-Fi, a phone hotspot), Private (home, office) or Domain.</summary>
    public enum NetworkCategory
    {
        Unknown = -1,
        Public = 0,
        Private = 1,
        Domain = 2
    }

    /// <summary>One network card that is connected, with the Windows network it is on.</summary>
    public sealed class NetworkConnection
    {
        /// <summary>The card's name in Windows ("Ethernet", "Wi-Fi").</summary>
        public string Adapter { get; set; }
        public NetworkInterfaceType AdapterType { get; set; }
        /// <summary>The card's IPv4 addresses.</summary>
        public List<string> Addresses { get; set; } = new List<string>();
        /// <summary>The network's name as Windows shows it ("Network 2", the Wi-Fi name, the phone's name).</summary>
        public string Network { get; set; }
        public Guid NetworkId { get; set; }
        public NetworkCategory Category { get; set; } = NetworkCategory.Unknown;

        public bool IsWireless
        {
            get { return AdapterType == NetworkInterfaceType.Wireless80211; }
        }
    }

    /// <summary>
    /// Which networks this PC is connected to and whether Windows treats each as Public or Private (0.5.0, the hotspot
    /// fix). The installer's firewall rules let other PCs in on Private and Domain networks only (brief, section 10),
    /// and a phone hotspot usually comes up as Public, so a PC sharing printers over one cannot be reached. Reading
    /// needs no rights; making a network Private needs an administrator, so only PrintVect.Elevate calls SetPrivate.
    /// The Network List Manager is a COM interface of Windows Vista and later; elsewhere the list is simply empty.
    /// </summary>
    public static class NetworkProfiles
    {
        private static readonly Guid NetworkListManagerClsid = new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
        private static string _lastProblem;

        /// <summary>Every connected network card with an IPv4 address, and its network (empty when Windows cannot say).</summary>
        public static IList<NetworkConnection> Connected()
        {
            var result = new List<NetworkConnection>();
            Dictionary<Guid, NetworkInterface> cards = Cards();
            if (!WindowsInfo.IsWindows)
            {
                return result;
            }
            object manager = null;
            try
            {
                manager = Activator.CreateInstance(Type.GetTypeFromCLSID(NetworkListManagerClsid, true));
                IEnumNetworkConnections connections = ((INetworkListManager)manager).GetNetworkConnections();
                try
                {
                    while (true)
                    {
                        INetworkConnection connection;
                        uint fetched;
                        if (connections.Next(1, out connection, out fetched) != 0 || fetched == 0 || connection == null)
                        {
                            break;
                        }
                        try
                        {
                            if (!connection.IsConnected) continue;
                            NetworkInterface card;
                            if (!cards.TryGetValue(connection.GetAdapterId(), out card)) continue;   // a card without IPv4
                            INetwork network = connection.GetNetwork();
                            try
                            {
                                result.Add(new NetworkConnection
                                {
                                    Adapter = card.Name,
                                    AdapterType = card.NetworkInterfaceType,
                                    Addresses = IPv4(card),
                                    Network = network.GetName(),
                                    NetworkId = network.GetNetworkId(),
                                    Category = ToCategory(network.GetCategory())
                                });
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(network);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(connection);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(connections);
                }
            }
            catch (Exception ex)
            {
                // The Share page asks every 30 seconds: the same problem goes to the log once.
                if (ex.Message != _lastProblem)
                {
                    _lastProblem = ex.Message;
                    Log.Warn("Windows did not say which networks this PC is on (Public or Private): " + ex.Message);
                }
            }
            finally
            {
                if (manager != null && Marshal.IsComObject(manager)) Marshal.ReleaseComObject(manager);
            }
            return result.OrderBy(c => c.Adapter, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The connected networks Windows treats as Public, each once (a network can have two cards on it).</summary>
        public static IList<NetworkConnection> PublicNetworks(IEnumerable<NetworkConnection> connected)
        {
            return connected.Where(c => c.Category == NetworkCategory.Public)
                            .GroupBy(c => c.NetworkId).Select(g => g.First()).ToList();
        }

        /// <summary>
        /// Makes one network Private (administrator only). Returns the network's name. Throws with a plain message when
        /// the network is not connected, is a domain network, or Windows refuses (a company policy can lock it).
        /// </summary>
        public static string SetPrivate(Guid networkId)
        {
            object manager = Activator.CreateInstance(Type.GetTypeFromCLSID(NetworkListManagerClsid, true));
            INetwork network = null;
            try
            {
                network = ((INetworkListManager)manager).GetNetwork(networkId);
                string name = network.GetName();
                NetworkCategory before = ToCategory(network.GetCategory());
                if (before == NetworkCategory.Domain)
                {
                    throw new InvalidOperationException("\"" + name + "\" is the company's domain network; Windows decides how it is treated.");
                }
                if (before != NetworkCategory.Private)
                {
                    network.SetCategory(NlmNetworkCategory.Private);
                }
                NetworkCategory after = ToCategory(network.GetCategory());
                if (after != NetworkCategory.Private)
                {
                    throw new InvalidOperationException("Windows kept \"" + name + "\" as " + after + " (a company policy may lock it).");
                }
                Log.Info("Network \"" + name + "\" (" + networkId + ") is now Private (was " + before + ").");
                return name;
            }
            finally
            {
                if (network != null) Marshal.ReleaseComObject(network);
                if (Marshal.IsComObject(manager)) Marshal.ReleaseComObject(manager);
            }
        }

        /// <summary>One line per connected card, for the Diagnostics report.</summary>
        public static IEnumerable<string> Describe(IEnumerable<NetworkConnection> connected)
        {
            var lines = connected.Select(c => c.Adapter + ": network \"" + c.Network + "\", " + c.Category
                + (c.Category == NetworkCategory.Public ? " (the firewall rules of PrintVect do not let other PCs in here)" : "")
                + ", " + (c.Addresses.Count == 0 ? "no IPv4 address" : string.Join(", ", c.Addresses))).ToList();
            if (lines.Count == 0)
            {
                lines.Add(WindowsInfo.IsWindows ? "Windows reports no connected network." : "Network categories: not Windows.");
            }
            return lines;
        }

        private static Dictionary<Guid, NetworkInterface> Cards()
        {
            var cards = new Dictionary<Guid, NetworkInterface>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    Guid id;
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || !Guid.TryParse(nic.Id, out id) || IPv4(nic).Count == 0)
                    {
                        continue;
                    }
                    cards[id] = nic;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list the network cards: " + ex.Message);
            }
            return cards;
        }

        private static List<string> IPv4(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPProperties().UnicastAddresses
                          .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                          .Select(a => a.Address.ToString()).ToList();
            }
            catch (NetworkInformationException)
            {
                return new List<string>();
            }
        }

        private static NetworkCategory ToCategory(NlmNetworkCategory category)
        {
            switch (category)
            {
                case NlmNetworkCategory.Public: return NetworkCategory.Public;
                case NlmNetworkCategory.Private: return NetworkCategory.Private;
                case NlmNetworkCategory.DomainAuthenticated: return NetworkCategory.Domain;
                default: return NetworkCategory.Unknown;
            }
        }

        // ---- The Network List Manager (netlistmgr.h). Methods in the exact order of the Windows interfaces. ----

        private enum NlmNetworkCategory
        {
            Public = 0,
            Private = 1,
            DomainAuthenticated = 2
        }

        [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkListManager
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetNetworks(int flags);
            [return: MarshalAs(UnmanagedType.Interface)]
            INetwork GetNetwork(Guid networkId);
            [return: MarshalAs(UnmanagedType.Interface)]
            IEnumNetworkConnections GetNetworkConnections();
        }

        [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetwork
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            string GetName();
            void SetName([MarshalAs(UnmanagedType.BStr)] string name);
            [return: MarshalAs(UnmanagedType.BStr)]
            string GetDescription();
            void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
            Guid GetNetworkId();
            int GetDomainType();
            [return: MarshalAs(UnmanagedType.Interface)]
            object GetNetworkConnections();
            void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);
            bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            int GetConnectivity();
            NlmNetworkCategory GetCategory();
            void SetCategory(NlmNetworkCategory category);
        }

        [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkConnection
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            INetwork GetNetwork();
            bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            int GetConnectivity();
            Guid GetConnectionId();
            Guid GetAdapterId();
        }

        [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IEnumNetworkConnections
        {
            object NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
            [PreserveSig]
            int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out INetworkConnection connection, out uint fetched);
        }
    }
}
