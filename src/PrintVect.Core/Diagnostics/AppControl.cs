using System;
using System.ComponentModel;
using Microsoft.Win32;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Diagnostics
{
    /// <summary>Smart App Control on Windows 11: Off, On (only trusted or signed programs run), or Evaluation.</summary>
    public enum SmartAppControlState
    {
        /// <summary>Windows 10 and older, or not Windows: there is no Smart App Control.</summary>
        NotAvailable,
        Off,
        On,
        /// <summary>Windows watches for a while and may switch it on by itself.</summary>
        Evaluation,
        Unknown
    }

    /// <summary>
    /// Windows rules that allow only approved programs to run (0.5.4). On the owner's Windows 11 PC Smart App Control was
    /// On and refused the unsigned setup program of an update: "An Application Control policy has blocked this file".
    /// This tells that case apart from other start failures, and reads Smart App Control's state for Diagnostics.
    /// </summary>
    public static class AppControl
    {
        /// <summary>ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION (0x800711C7): Smart App Control or an App Control policy.</summary>
        public const int PolicyViolation = 4551;

        /// <summary>ERROR_ACCESS_DISABLED_BY_POLICY: an AppLocker or software restriction policy.</summary>
        public const int DisabledByPolicy = 1260;

        private const string PolicyKey = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
        private const string StateValue = "VerifiedAndReputablePolicyState";

        /// <summary>True when Windows refused to start a program because a rule allows only approved ones.</summary>
        public static bool IsBlockedByPolicy(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                var win32 = e as Win32Exception;
                if (win32 != null && (win32.NativeErrorCode == PolicyViolation || win32.NativeErrorCode == DisabledByPolicy))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Smart App Control's state from the registry; NotAvailable where Windows has none. Never throws.</summary>
        public static SmartAppControlState ReadSmartAppControl()
        {
            if (!WindowsInfo.IsWindows)
            {
                return SmartAppControlState.NotAvailable;
            }
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(PolicyKey))
                {
                    return FromValue(key == null ? null : key.GetValue(StateValue));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the Smart App Control state: " + ex.Message);
                return SmartAppControlState.Unknown;
            }
        }

        /// <summary>The registry value: missing = not available, 0 = Off, 1 = On, 2 = Evaluation.</summary>
        public static SmartAppControlState FromValue(object value)
        {
            if (value == null)
            {
                return SmartAppControlState.NotAvailable;
            }
            if (!(value is int))
            {
                return SmartAppControlState.Unknown;
            }
            switch ((int)value)
            {
                case 0: return SmartAppControlState.Off;
                case 1: return SmartAppControlState.On;
                case 2: return SmartAppControlState.Evaluation;
                default: return SmartAppControlState.Unknown;
            }
        }

        /// <summary>One line for the Diagnostics report.</summary>
        public static string Describe(SmartAppControlState state)
        {
            switch (state)
            {
                case SmartAppControlState.On:
                    return "Smart App Control: On (only trusted or signed programs run; PrintVect's setup program is not signed, so updates from inside PrintVect are blocked)";
                case SmartAppControlState.Evaluation:
                    return "Smart App Control: Evaluation (Windows may switch it on by itself)";
                case SmartAppControlState.Off:
                    return "Smart App Control: Off";
                case SmartAppControlState.NotAvailable:
                    return "Smart App Control: not available on this Windows";
                default:
                    return "Smart App Control: unknown";
            }
        }
    }
}
