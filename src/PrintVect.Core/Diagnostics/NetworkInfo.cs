using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    /// <summary>The PC's network cards and IPv4 addresses (the office LAN is IPv4).</summary>
    public static class NetworkInfo
    {
        public static IEnumerable<string> Describe()
        {
            var lines = new List<string>();
            try
            {
                lines.Add("Host name: " + Dns.GetHostName());
            }
            catch (Exception ex)
            {
                lines.Add("Host name: unknown (" + ex.Message + ")");
            }

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var addresses = new List<string>();
                    try
                    {
                        addresses.AddRange(nic.GetIPProperties().UnicastAddresses
                            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(a => a.Address.ToString()));
                    }
                    catch (Exception ex)
                    {
                        addresses.Add("addresses not readable: " + ex.Message);
                    }

                    lines.Add(string.Format("{0} [{1}, {2}]: {3}", nic.Name, nic.NetworkInterfaceType,
                        nic.OperationalStatus, addresses.Count == 0 ? "no IPv4 address" : string.Join(", ", addresses)));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not list network cards: " + ex.Message);
                lines.Add("Network cards could not be listed: " + ex.Message);
            }

            if (lines.Count == 1)
            {
                lines.Add("No network cards found.");
            }
            return lines;
        }

        /// <summary>Every IPv4 address on a card that is up (used later to bind the listeners).</summary>
        public static IList<IPAddress> LocalIPv4Addresses()
        {
            var result = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            result.Add(address.Address);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read local IPv4 addresses: " + ex.Message);
            }
            return result;
        }
    }
}
