using System;
using Microsoft.Win32;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Config
{
    /// <summary>
    /// "Start with Windows": the HKLM Run entry the installer creates (PrintVect = "...\PrintVect.App.exe" /tray, for every
    /// user). Reading it needs no rights; changing it needs administrator rights, so the Settings switch asks
    /// PrintVect.Elevate to do it. The 64-bit registry view is used on 64-bit Windows, as the installer does.
    /// </summary>
    public static class Autostart
    {
        public const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "PrintVect";
        public const string TraySwitch = "/tray";

        /// <summary>True when Windows starts PrintVect at sign-in; null when the registry cannot be read (not Windows).</summary>
        public static bool? IsOn()
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, View))
                using (RegistryKey run = root.OpenSubKey(RunKey))
                {
                    return run != null && !string.IsNullOrWhiteSpace(run.GetValue(ValueName) as string);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the Start with Windows entry: " + ex.Message);
                return null;
            }
        }

        /// <summary>What the Run entry holds for <paramref name="appExePath"/>.</summary>
        public static string CommandFor(string appExePath)
        {
            return "\"" + appExePath + "\" " + TraySwitch;
        }

        /// <summary>Creates or deletes the Run entry. Needs administrator rights (PrintVect.Elevate).</summary>
        public static void Set(bool on, string appExePath)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, View))
            using (RegistryKey run = root.CreateSubKey(RunKey))
            {
                if (run == null)
                {
                    throw new InvalidOperationException("The Windows Run key could not be opened.");
                }
                if (on)
                {
                    run.SetValue(ValueName, CommandFor(appExePath), RegistryValueKind.String);
                }
                else if (run.GetValue(ValueName) != null)
                {
                    run.DeleteValue(ValueName, false);
                }
            }
        }

        private static RegistryView View
        {
            get { return Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default; }
        }
    }
}
