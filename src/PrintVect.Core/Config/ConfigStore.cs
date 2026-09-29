using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Config
{
    /// <summary>Loads and saves config.json. Never throws on a bad file: it keeps a copy and starts fresh.</summary>
    public sealed class ConfigStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public ConfigStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A config file path is required.", nameof(path));
            }
            Path = path;
        }

        public string Path { get; }

        /// <summary>Loads the file, or creates it with defaults when it does not exist yet.</summary>
        public AppConfig LoadOrCreate()
        {
            if (File.Exists(Path))
            {
                return Load();
            }

            var config = new AppConfig();
            Log.Info("Config: " + Path + " does not exist yet; creating it with default settings.");
            try
            {
                Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("Config: could not create " + Path + "; continuing with default settings in memory.", ex);
            }
            return config;
        }

        /// <summary>Loads the file. A missing or unreadable file yields defaults (and a log entry).</summary>
        public AppConfig Load()
        {
            if (!File.Exists(Path))
            {
                Log.Info("Config: " + Path + " not found; using default settings.");
                return new AppConfig();
            }

            AppConfig config;
            try
            {
                string json = File.ReadAllText(Path, Encoding.UTF8);
                config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                string backup = KeepBadFile();
                Log.Error("Config: " + Path + " could not be read; starting with default settings. "
                          + (backup == null ? "" : "The old file was kept as " + backup + "."), ex);
                return new AppConfig();
            }

            foreach (string warning in Normalize(config))
            {
                Log.Warn("Config: " + warning);
            }
            return config;
        }

        /// <summary>Writes the file. Writes to a temporary file first so a crash never leaves half a file.</summary>
        public void Save(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            Normalize(config);

            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = Path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(config, Formatting.Indented), Utf8NoBom);
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
            File.Move(temp, Path);
        }

        /// <summary>
        /// Repairs values a hand-edited or older file may contain and returns one plain-language
        /// warning per repair. Never throws.
        /// </summary>
        public static IList<string> Normalize(AppConfig config)
        {
            var warnings = new List<string>();
            if (config == null)
            {
                return warnings;
            }

            if (!PortRules.IsAllowed(config.DiscoveryPort))
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "discovery port {0} is not allowed (use {1}-{2}, never {3} or {4}); using {5}.",
                    config.DiscoveryPort, PortRules.MinPort, PortRules.MaxPort, PortRules.RawRelayPort, PortRules.IppPort,
                    AppConfig.DefaultDiscoveryPort));
                config.DiscoveryPort = AppConfig.DefaultDiscoveryPort;
            }

            if (!PortRules.IsAllowed(config.JobPort))
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "job port {0} is not allowed (use {1}-{2}, never {3} or {4}); using {5}.",
                    config.JobPort, PortRules.MinPort, PortRules.MaxPort, PortRules.RawRelayPort, PortRules.IppPort,
                    AppConfig.DefaultJobPort));
                config.JobPort = AppConfig.DefaultJobPort;
            }

            if (config.DiscoveryPort == config.JobPort)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "discovery and job ports must be different (both were {0}); using {1} and {2}.",
                    config.JobPort, AppConfig.DefaultDiscoveryPort, AppConfig.DefaultJobPort));
                config.DiscoveryPort = AppConfig.DefaultDiscoveryPort;
                config.JobPort = AppConfig.DefaultJobPort;
            }

            if (config.KeepSentFilesHours < 1 || config.KeepSentFilesHours > AppConfig.MaxKeepSentFilesHours)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "keep-sent-files hours {0} is outside 1-{1}; using {2}.",
                    config.KeepSentFilesHours, AppConfig.MaxKeepSentFilesHours, AppConfig.DefaultKeepSentFilesHours));
                config.KeepSentFilesHours = AppConfig.DefaultKeepSentFilesHours;
            }

            if (config.Pin == null)
            {
                config.Pin = "";
            }
            config.Pin = config.Pin.Trim();

            if (string.IsNullOrWhiteSpace(config.Language))
            {
                config.Language = AppConfig.DefaultLanguage;
            }

            if (config.SharedPrinters == null)
            {
                config.SharedPrinters = new List<SharedPrinter>();
            }
            if (config.RemotePrinters == null)
            {
                config.RemotePrinters = new List<RemotePrinter>();
            }

            if (config.Version > AppConfig.CurrentVersion)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "the file was written by a newer PrintVect (file version {0}, this program understands {1}); unknown settings are ignored.",
                    config.Version, AppConfig.CurrentVersion));
            }
            config.Version = AppConfig.CurrentVersion;

            return warnings;
        }

        private string KeepBadFile()
        {
            try
            {
                string backup = Path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Copy(Path, backup, true);
                return backup;
            }
            catch (Exception ex)
            {
                Log.Warn("Config: could not keep a copy of the unreadable file: " + ex.Message);
                return null;
            }
        }
    }
}
