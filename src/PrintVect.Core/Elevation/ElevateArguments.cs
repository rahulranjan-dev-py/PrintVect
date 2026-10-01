using System;
using System.Collections.Generic;
using System.Text;

namespace PrintVect.Core.Elevation
{
    /// <summary>The commands PrintVect.Elevate.exe understands.</summary>
    public static class ElevateCommands
    {
        public const string Ping = "ping";
        public const string Version = "version";
        /// <summary>Creates the spool folder, the Local Port and the virtual printer for one remote printer.</summary>
        public const string AddPrinter = "add-printer";
        /// <summary>Deletes the virtual printer, then its port, then its spool folder.</summary>
        public const string RemovePrinter = "remove-printer";
        /// <summary>Removes every "PrintVect - ..." printer and port (used by the uninstaller, M5).</summary>
        public const string RemoveAll = "remove-all";

        public const string OptionResult = "result";
        public const string OptionName = "name";
        public const string OptionPort = "port";
        public const string OptionFolder = "folder";
        public const string OptionDriver = "driver";
        public const string OptionId = "id";
    }

    /// <summary>
    /// Builds and parses the command line of PrintVect.Elevate.exe:
    /// &lt;command&gt; [/option value]... Options take one value each; values with spaces are quoted.
    /// </summary>
    public sealed class ElevateArguments
    {
        public string Command { get; set; }
        public Dictionary<string, string> Options { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string option)
        {
            string value;
            return Options.TryGetValue(option, out value) ? value : null;
        }

        /// <summary>The argument string for ProcessStartInfo.Arguments.</summary>
        public string ToCommandLine()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Command)) parts.Add(Quote(Command));
            foreach (KeyValuePair<string, string> pair in Options)
            {
                if (pair.Value == null) continue;
                parts.Add("/" + pair.Key);
                parts.Add(Quote(pair.Value));
            }
            return string.Join(" ", parts);
        }

        public static ElevateArguments Parse(string[] args)
        {
            var result = new ElevateArguments();
            if (args == null) return result;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? "";
                if ((arg.StartsWith("/", StringComparison.Ordinal) || arg.StartsWith("-", StringComparison.Ordinal)) && arg.Length > 1)
                {
                    string key = arg.TrimStart('/', '-');
                    string value = i + 1 < args.Length ? args[++i] : "";
                    result.Options[key] = value;
                }
                else if (result.Command == null)
                {
                    result.Command = arg.ToLowerInvariant();
                }
            }
            return result;
        }

        /// <summary>Windows command-line quoting: wrap in quotes, escape embedded quotes and the backslashes before them.</summary>
        public static string Quote(string value)
        {
            if (value == null) value = "";
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes).Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
