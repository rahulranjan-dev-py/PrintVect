using System.Collections.Generic;

namespace PrintVect.Core.Config
{
    /// <summary>
    /// Everything PrintVect remembers between runs. Stored as %ProgramData%\PrintVect\config.json.
    /// Keep every property simple (numbers, strings, bools, lists) so the file stays hand-editable.
    /// </summary>
    public sealed class AppConfig
    {
        public const int CurrentVersion = 1;
        public const int DefaultDiscoveryPort = 9150;
        public const int DefaultJobPort = 9151;
        public const int DefaultKeepSentFilesHours = 1;
        public const int MaxKeepSentFilesHours = 168;
        public const string DefaultLanguage = "en";

        /// <summary>Schema version of this file.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>UDP port used for "who has a printer?" broadcasts.</summary>
        public int DiscoveryPort { get; set; } = DefaultDiscoveryPort;

        /// <summary>TCP port on which a host receives print jobs.</summary>
        public int JobPort { get; set; } = DefaultJobPort;

        /// <summary>Optional shared PIN. Empty means open to everyone on the office network.</summary>
        public string Pin { get; set; } = "";

        /// <summary>Whether this PC currently offers its printers to the office (host role).</summary>
        public bool SharingEnabled { get; set; }

        /// <summary>Whether PrintVect should start with Windows (wired up by the installer, M5).</summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>How long sent job files are kept for diagnostics before they are deleted.</summary>
        public int KeepSentFilesHours { get; set; } = DefaultKeepSentFilesHours;

        /// <summary>UI language. "en" now, "hi" in Phase 2.</summary>
        public string Language { get; set; } = DefaultLanguage;

        /// <summary>Local printers this PC shares (host role, M1).</summary>
        public List<SharedPrinter> SharedPrinters { get; set; } = new List<SharedPrinter>();

        /// <summary>Remote printers added to this PC (client role, M2/M3).</summary>
        public List<RemotePrinter> RemotePrinters { get; set; } = new List<RemotePrinter>();
    }

    /// <summary>A printer installed on this PC that is offered to the office.</summary>
    public sealed class SharedPrinter
    {
        /// <summary>Stable id sent to clients (a GUID string).</summary>
        public string Id { get; set; }

        /// <summary>The Windows printer name on the host, e.g. "HP LaserJet 1020".</summary>
        public string LocalName { get; set; }

        /// <summary>What clerks see, e.g. "Counter 1 Laser".</summary>
        public string FriendlyName { get; set; }
    }

    /// <summary>A printer on another PC that has been added to this PC as a virtual printer.</summary>
    public sealed class RemotePrinter
    {
        public string PrinterId { get; set; }
        public string HostName { get; set; }
        public string HostIp { get; set; }
        public int Port { get; set; } = AppConfig.DefaultJobPort;

        /// <summary>Name of the virtual printer on this PC, e.g. "PrintVect - Counter 1 Laser @COUNTER1".</summary>
        public string LocalPrinterName { get; set; }
        public string FriendlyName { get; set; }
    }
}
