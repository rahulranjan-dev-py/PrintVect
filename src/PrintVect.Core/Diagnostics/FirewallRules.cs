using System;
using System.Collections.Generic;
using System.Diagnostics;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    public enum FirewallRuleState
    {
        Present,
        Missing,
        Unknown
    }

    /// <summary>
    /// The Windows Firewall rules PrintVect needs (created by the installer / Elevate helper in M5).
    /// The names are fixed here so every part of the program looks for the same rules.
    /// Only netsh's exit code is used, so this works whatever language Windows displays.
    /// </summary>
    public static class FirewallRules
    {
        public const string JobsRuleName = "PrintVect Jobs (TCP-In)";
        public const string DiscoveryRuleName = "PrintVect Discovery (UDP-In)";
        public const int NetshTimeoutMilliseconds = 5000;

        public static IEnumerable<string> Describe(AppConfig config)
        {
            var lines = new List<string>();
            int jobPort = config == null ? AppConfig.DefaultJobPort : config.JobPort;
            int discoveryPort = config == null ? AppConfig.DefaultDiscoveryPort : config.DiscoveryPort;
            lines.Add(DescribeRule(JobsRuleName, "TCP " + jobPort));
            lines.Add(DescribeRule(DiscoveryRuleName, "UDP " + discoveryPort));
            return lines;
        }

        private static string DescribeRule(string ruleName, string port)
        {
            string detail;
            FirewallRuleState state = Query(ruleName, out detail);
            switch (state)
            {
                case FirewallRuleState.Present:
                    return ruleName + " for " + port + ": present";
                case FirewallRuleState.Missing:
                    return ruleName + " for " + port + ": MISSING (other PCs cannot reach this one until the installer adds it)";
                default:
                    return ruleName + " for " + port + ": could not check (" + detail + ")";
            }
        }

        /// <summary>Asks netsh whether an inbound rule with this exact name exists.</summary>
        public static FirewallRuleState Query(string ruleName, out string detail)
        {
            if (!WindowsInfo.IsWindows)
            {
                detail = "not Windows";
                return FirewallRuleState.Unknown;
            }

            try
            {
                var startInfo = new ProcessStartInfo("netsh.exe",
                    "advfirewall firewall show rule name=\"" + ruleName.Replace("\"", "") + "\" dir=in")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        detail = "netsh did not start";
                        return FirewallRuleState.Unknown;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(NetshTimeoutMilliseconds))
                    {
                        try { process.Kill(); } catch (Exception killEx) { Log.Warn("Could not stop netsh: " + killEx.Message); }
                        detail = "netsh did not answer within " + (NetshTimeoutMilliseconds / 1000) + " s";
                        return FirewallRuleState.Unknown;
                    }

                    switch (process.ExitCode)
                    {
                        case 0:
                            detail = "found";
                            return FirewallRuleState.Present;
                        case 1:
                            detail = "no rule with this name";
                            return FirewallRuleState.Missing;
                        default:
                            detail = "netsh exit code " + process.ExitCode + " " + (output + " " + error).Trim();
                            return FirewallRuleState.Unknown;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Firewall check for rule '" + ruleName + "' failed: " + ex.Message);
                detail = ex.Message;
                return FirewallRuleState.Unknown;
            }
        }
    }
}
