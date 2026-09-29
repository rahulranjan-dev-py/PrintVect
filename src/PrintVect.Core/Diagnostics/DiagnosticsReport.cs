using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    /// <summary>
    /// The text behind the Diagnostics tab's "Copy to clipboard" button (brief, section 7).
    /// It is a technical report for whoever supports PrintVect, so it is English only and is
    /// never sent anywhere by the program itself. It must never contain the PIN.
    /// </summary>
    public sealed class DiagnosticsReport
    {
        public const int DefaultLogLineCount = 200;

        private readonly StringBuilder _text = new StringBuilder();

        public DiagnosticsReport()
        {
            _text.Append(AppInfo.ProductName).Append(" diagnostics, generated ")
                 .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                 .AppendLine();
        }

        public DiagnosticsReport AddSection(string title, IEnumerable<string> lines)
        {
            _text.AppendLine();
            _text.Append("== ").Append(title).Append(" ==").AppendLine();
            bool any = false;
            if (lines != null)
            {
                foreach (string line in lines)
                {
                    _text.AppendLine(line);
                    any = true;
                }
            }
            if (!any)
            {
                _text.AppendLine("(nothing)");
            }
            return this;
        }

        public DiagnosticsReport AddSection(string title, string body)
        {
            return AddSection(title, string.IsNullOrEmpty(body) ? null : new[] { body });
        }

        public override string ToString()
        {
            return _text.ToString();
        }

        /// <summary>
        /// Collects everything Core knows about. <paramref name="beforeLogs"/> lets the caller add
        /// sections that need UI assemblies (the printer list) before the log tail, which comes last.
        /// </summary>
        public static DiagnosticsReport Collect(AppPaths paths, AppConfig config,
            Action<DiagnosticsReport> beforeLogs = null, int logLineCount = DefaultLogLineCount)
        {
            var report = new DiagnosticsReport();
            report.AddSection("Program", DescribeProgram());
            report.AddSection("Windows", WindowsInfo.Describe());
            report.AddSection("Network", NetworkInfo.Describe());
            report.AddSection("Settings", DescribeConfig(paths, config));
            report.AddSection("Folders", DescribeFolders(paths));
            report.AddSection("Firewall rules", FirewallRules.Describe(config));

            if (beforeLogs != null)
            {
                try
                {
                    beforeLogs(report);
                }
                catch (Exception ex)
                {
                    Log.Error("Diagnostics: an extra section failed.", ex);
                    report.AddSection("Extra section failed", ex.ToString());
                }
            }

            report.AddSection("Log files", DescribeLogFiles(paths));
            report.AddSection("Last " + logLineCount + " log lines", Log.TailLines(logLineCount));
            return report;
        }

        private static IEnumerable<string> DescribeProgram()
        {
            var lines = new List<string>();
            lines.Add(AppInfo.ProductName + " " + AppInfo.Version);
            try
            {
                lines.Add("Program file: " + typeof(DiagnosticsReport).Assembly.Location);
            }
            catch (Exception ex)
            {
                lines.Add("Program file: unknown (" + ex.Message + ")");
            }
            lines.Add("Process: " + (Environment.Is64BitProcess ? "64-bit" : "32-bit")
                      + ", started " + System.Diagnostics.Process.GetCurrentProcess().StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                      + ", PID " + System.Diagnostics.Process.GetCurrentProcess().Id);
            return lines;
        }

        private static IEnumerable<string> DescribeConfig(AppPaths paths, AppConfig config)
        {
            var lines = new List<string>();
            lines.Add("File: " + paths.ConfigFile + (File.Exists(paths.ConfigFile) ? "" : " (does not exist yet)"));
            if (config == null)
            {
                lines.Add("Settings not loaded.");
                return lines;
            }
            lines.Add("Discovery port (UDP): " + config.DiscoveryPort + ", job port (TCP): " + config.JobPort);
            lines.Add("PIN: " + (string.IsNullOrEmpty(config.Pin) ? "not set (open to the office network)" : "set"));
            lines.Add("Sharing: " + (config.SharingEnabled ? "ON" : "OFF")
                      + ", shared printers: " + config.SharedPrinters.Count
                      + ", remote printers added: " + config.RemotePrinters.Count);
            lines.Add("Start with Windows: " + (config.StartWithWindows ? "yes" : "no")
                      + ", keep sent files: " + config.KeepSentFilesHours + " h, language: " + config.Language);
            return lines;
        }

        private static IEnumerable<string> DescribeFolders(AppPaths paths)
        {
            var lines = new List<string>();
            lines.Add(DescribeFolder("Data", paths.Root));
            lines.Add(DescribeFolder("Spool", paths.SpoolDir));
            lines.Add(DescribeFolder("Logs", paths.LogsDir));
            return lines;
        }

        private static string DescribeFolder(string label, string path)
        {
            if (!Directory.Exists(path))
            {
                return label + ": " + path + " (missing)";
            }

            string probe = Path.Combine(path, ".write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return label + ": " + path + " (ok, writable)";
            }
            catch (Exception ex)
            {
                return label + ": " + path + " (NOT writable: " + ex.Message + ")";
            }
        }

        private static IEnumerable<string> DescribeLogFiles(AppPaths paths)
        {
            var lines = new List<string>();
            if (!Directory.Exists(paths.LogsDir))
            {
                lines.Add("Log folder missing: " + paths.LogsDir);
                return lines;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(paths.LogsDir, "*.log");
            }
            catch (Exception ex)
            {
                lines.Add("Could not list " + paths.LogsDir + ": " + ex.Message);
                return lines;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}  {1:N0} bytes  last written {2:yyyy-MM-dd HH:mm}",
                        info.Name, info.Length, info.LastWriteTime));
                }
                catch (Exception ex)
                {
                    lines.Add(Path.GetFileName(file) + "  (" + ex.Message + ")");
                }
            }
            return lines;
        }
    }
}
