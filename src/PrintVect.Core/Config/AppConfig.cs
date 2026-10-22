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
        public const string ThemeSystem = "system";
        public const string ThemeLight = "light";
        public const string ThemeDark = "dark";

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

        /// <summary>
        /// Ask how many copies before a job printed on this PC is sent to another PC (M6b-2). The XPS writer usually
        /// loses the copies chosen in a program's print window (brief 11.6), so this is on by default.
        /// </summary>
        public bool AskCopies { get; set; } = true;

        /// <summary>Colours of the window (0.5.0): "system" follows Windows' light or dark app mode, or "light", "dark".</summary>
        public string Theme { get; set; } = ThemeSystem;

        /// <summary>UI language. "en" now, "hi" in Phase 2.</summary>
        public string Language { get; set; } = DefaultLanguage;

        /// <summary>
        /// Ask github.com once a day whether a newer PrintVect has been released (the only time
        /// PrintVect uses the internet; printing never does). Off means "Check now" only.
        /// </summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>The version that ran last time, so the first start after an update can say so.</summary>
        public string LastRunVersion { get; set; } = "";

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

        /// <summary>Paused on this PC: jobs for it are accepted and wait until someone presses Resume (M6b-2).</summary>
        public bool Paused { get; set; }
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
