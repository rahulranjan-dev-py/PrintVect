using System;
using System.IO;
using Newtonsoft.Json;
using PrintVect.Core.Config;

namespace PrintVect.Core.Elevation
{
    /// <summary>Exit codes of PrintVect.Elevate.exe (brief, section 11.4).</summary>
    public static class ElevateExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int UsageError = 2;
        public const int NotElevated = 3;
    }

    /// <summary>
    /// What PrintVect.Elevate.exe writes to spool\elevate-result.json when it finishes, so the
    /// main app (which cannot read the elevated process's console) learns what happened.
    /// </summary>
    public sealed class ElevateResult
    {
        public const string DefaultFileName = "elevate-result.json";

        public string Command { get; set; } = "";
        public bool Ok { get; set; }
        public int ExitCode { get; set; } = ElevateExitCodes.Failed;
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public static string DefaultFile(AppPaths paths)
        {
            return Path.Combine(paths.SpoolDir, DefaultFileName);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public static ElevateResult FromJson(string json)
        {
            return JsonConvert.DeserializeObject<ElevateResult>(json);
        }
    }
}
