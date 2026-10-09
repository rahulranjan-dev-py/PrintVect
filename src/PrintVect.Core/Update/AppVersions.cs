using System;
using System.Globalization;

namespace PrintVect.Core.Update
{
    /// <summary>
    /// Version arithmetic for the update check. PrintVect versions are three numbers ("0.2.1",
    /// from Directory.Build.props) and release tags carry a leading "v" ("v0.2.1"). Everything is
    /// compared on those three numbers, so "1.0.0" and "1.0.0.0" are the same version.
    /// </summary>
    public static class AppVersions
    {
        /// <summary>The version of the running program, as three numbers.</summary>
        public static Version Current
        {
            get { return Parse(AppInfo.Version) ?? new Version(0, 0, 0); }
        }

        /// <summary>"v0.2.1", "0.2.1", "0.2.1.0" or "0.2" give 0.2.1 / 0.2.0; null when the text is not a version.</summary>
        public static Version Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1);
            }
            // Ignore anything after the numbers, such as "-beta" or "+abc123".
            int cut = s.IndexOfAny(new[] { '+', '-', ' ' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }

            string[] parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4)
            {
                return null;
            }
            var numbers = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n))
                {
                    return null;
                }
                if (i < numbers.Length)
                {
                    numbers[i] = n;
                }
            }
            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        /// <summary>Three numbers, never -1 in the third place.</summary>
        public static Version Normalize(Version v)
        {
            return v == null ? null : new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }

        public static bool IsNewer(Version candidate, Version current)
        {
            if (candidate == null)
            {
                return false;
            }
            if (current == null)
            {
                return true;
            }
            return Normalize(candidate) > Normalize(current);
        }

        public static string Format(Version v)
        {
            return v == null ? "?" : Normalize(v).ToString(3);
        }
    }
}
