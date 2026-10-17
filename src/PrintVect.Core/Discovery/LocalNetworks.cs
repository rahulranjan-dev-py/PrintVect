using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Discovery
{
    /// <summary>One IPv4 address of this PC with its subnet mask and directed broadcast address.</summary>
    public sealed class LocalNetwork
    {
        public IPAddress Address { get; set; }
        public IPAddress Mask { get; set; }
        public IPAddress Broadcast { get; set; }
        public string InterfaceName { get; set; }

        public bool Contains(IPAddress other)
        {
            return LocalNetworks.SameSubnet(Address, other, Mask);
        }

        public override string ToString()
        {
            return Address + "/" + Mask + " (" + InterfaceName + ")";
        }
    }

    /// <summary>
    /// The IPv4 networks this PC is on. The owner's host has two cards on different subnets, so
    /// discovery must go out on every one of them and the host must answer from the matching one.
    /// </summary>
    public static class LocalNetworks
    {
        public static IList<LocalNetwork> List()
        {
            var result = new List<LocalNetwork>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        IPAddress mask = address.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                        result.Add(new LocalNetwork
                        {
                            Address = address.Address,
                            Mask = mask,
                            Broadcast = BroadcastOf(address.Address, mask),
                            InterfaceName = nic.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list the network cards: " + ex.Message);
            }
            return result;
        }

        public static IPAddress BroadcastOf(IPAddress address, IPAddress mask)
        {
            byte[] a = address.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            var b = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                b[i] = (byte)(a[i] | (byte)~m[i]);
            }
            return new IPAddress(b);
        }

        public static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
        {
            if (a == null || b == null || mask == null) return false;
            if (a.AddressFamily != AddressFamily.InterNetwork || b.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] x = a.GetAddressBytes();
            byte[] y = b.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();
            for (int i = 0; i < 4; i++)
            {
                if ((x[i] & m[i]) != (y[i] & m[i])) return false;
            }
            return true;
        }

        /// <summary>The local address on the same subnet as <paramref name="remote"/>, else the first one, else null.</summary>
        public static IPAddress BestLocalAddressFor(IPAddress remote, IList<LocalNetwork> networks)
        {
            if (networks == null || networks.Count == 0) return null;
            if (remote != null && IPAddress.IsLoopback(remote)) return IPAddress.Loopback;
            LocalNetwork match = remote == null ? null : networks.FirstOrDefault(n => n.Contains(remote));
            return (match ?? networks[0]).Address;
        }
    }
}
