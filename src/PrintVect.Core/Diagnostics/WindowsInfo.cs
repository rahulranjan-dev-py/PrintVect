using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    /// <summary>Which Windows and which .NET Framework this PC runs (for the Diagnostics tab and the log).</summary>
    public static class WindowsInfo
    {
        public static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        /// <summary>One line for the start of the log, e.g. "Windows 11 Pro 23H2 (build 22631.4037), 64-bit, .NET Framework 4.8.1, PC COUNTER1, user spm".</summary>
        public static string OneLine()
        {
            return Edition() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")
                   + ", .NET Framework " + NetFrameworkInfo.Describe()
                   + ", PC " + Environment.MachineName + ", user " + Environment.UserName;
        }

        public static IEnumerable<string> Describe()
        {
            var lines = new List<string>();
            lines.Add("Edition: " + Edition());
            lines.Add("Reported by .NET: " + Environment.OSVersion.VersionString
                      + " (" + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + " Windows)");
            lines.Add(".NET Framework: " + NetFrameworkInfo.Describe() + ", CLR " + Environment.Version);
            lines.Add("PC name: " + Environment.MachineName + ", user: " + Environment.UserName
                      + (Environment.UserInteractive ? "" : " (no desktop session)"));
            lines.Add("Display language: " + CultureInfo.CurrentUICulture.Name
                      + ", formats: " + CultureInfo.CurrentCulture.Name
                      + ", up for " + FormatUptime());
            return lines;
        }

        /// <summary>"Windows 7 Professional Service Pack 1 (build 7601)" or "Windows 11 Pro 23H2 (build 22631.4037)".</summary>
        public static string Edition()
        {
            if (!IsWindows)
            {
                return "not Windows (" + Environment.OSVersion.VersionString + ")";
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null)
                    {
                        return "Windows " + Environment.OSVersion.Version;
                    }

                    string product = key.GetValue("ProductName") as string ?? "Windows";
                    string servicePack = key.GetValue("CSDVersion") as string;
                    string displayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
                    string build = key.GetValue("CurrentBuild") as string ?? key.GetValue("CurrentBuildNumber") as string;
                    object ubr = key.GetValue("UBR");

                    int buildNumber;
                    if (int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out buildNumber)
                        && buildNumber >= 22000 && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                    {
                        // Windows 11 still reports "Windows 10 ..." in ProductName.
                        product = "Windows 11" + product.Substring("Windows 10".Length);
                    }

                    var text = product;
                    if (!string.IsNullOrEmpty(servicePack))
                    {
                        text += " " + servicePack;
                    }
                    if (!string.IsNullOrEmpty(displayVersion))
                    {
                        text += " " + displayVersion;
                    }
                    if (!string.IsNullOrEmpty(build))
                    {
                        text += " (build " + build + (ubr != null ? "." + ubr : "") + ")";
                    }
                    return text;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the Windows version from the registry: " + ex.Message);
                return "Windows " + Environment.OSVersion.Version + " (registry not readable)";
            }
        }

        private static string FormatUptime()
        {
            // Environment.TickCount wraps after about 49 days when read unsigned; good enough for a hint.
            var uptime = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount));
            return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h {2}m", uptime.Days, uptime.Hours, uptime.Minutes);
        }
    }

    /// <summary>.NET Framework 4.x version from the registry "Release" value (brief, section 10).</summary>
    public static class NetFrameworkInfo
    {
        public const int Release48 = 528040;

        public static string Describe()
        {
            if (!WindowsInfo.IsWindows)
            {
                return "not Windows, runtime " + Environment.Version;
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    object value = key == null ? null : key.GetValue("Release");
                    if (!(value is int))
                    {
                        return "4.x, Release value not found (4.8 required)";
                    }
                    int release = (int)value;
                    return NameForRelease(release) + " (Release " + release + ")";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the .NET Framework version from the registry: " + ex.Message);
                return "unknown (" + ex.Message + ")";
            }
        }

        /// <summary>Maps the registry "Release" number to a version name.</summary>
        public static string NameForRelease(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= Release48) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 461308) return "4.7.1";
            if (release >= 460798) return "4.7";
            if (release >= 394802) return "4.6.2";
            if (release >= 394254) return "4.6.1";
            if (release >= 393295) return "4.6";
            if (release >= 379893) return "4.5.2";
            if (release >= 378675) return "4.5.1";
            if (release >= 378389) return "4.5";
            return "older than 4.5";
        }
    }
}
