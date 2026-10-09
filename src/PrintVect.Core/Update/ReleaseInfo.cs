using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PrintVect.Core.Update
{
    /// <summary>One file attached to a GitHub release.</summary>
    public sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// What the update check needs from GitHub's "latest release" answer
    /// (https://api.github.com/repos/OWNER/REPO/releases/latest): the version from the tag, the
    /// setup program among the attached files, and its ".sha256" checksum file.
    /// </summary>
    public sealed class ReleaseInfo
    {
        public const string InstallerPrefix = "PrintVect-Setup-";
        public const string InstallerExtension = ".exe";
        public const string HashExtension = ".sha256";

        private static readonly Regex InstallerName = new Regex(@"^PrintVect-Setup-[0-9][0-9.]*\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Tag { get; set; }
        public Version Version { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string PageUrl { get; set; }
        public bool Draft { get; set; }
        public bool PreRelease { get; set; }
        public DateTime? PublishedAt { get; set; }
        public List<ReleaseAsset> Assets { get; } = new List<ReleaseAsset>();

        /// <summary>The setup program (PrintVect-Setup-VERSION.exe), or null when the release has none.</summary>
        public ReleaseAsset Installer { get; set; }

        /// <summary>The checksum file next to it (same name plus .sha256), or null when there is none.</summary>
        public ReleaseAsset InstallerHash { get; set; }

        /// <summary>Reads GitHub's release JSON. Throws FormatException when it is not a release.</summary>
        public static ReleaseInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("The release answer is empty.");
            }
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new FormatException("The release answer is not JSON: " + ex.Message, ex);
            }

            var info = new ReleaseInfo
            {
                Tag = (string)root["tag_name"] ?? "",
                Title = (string)root["name"] ?? "",
                Notes = (string)root["body"] ?? "",
                PageUrl = (string)root["html_url"] ?? "",
                Draft = (bool?)root["draft"] ?? false,
                PreRelease = (bool?)root["prerelease"] ?? false
            };

            string publishedText = (string)root["published_at"];
            DateTime published;
            if (!string.IsNullOrEmpty(publishedText) && DateTime.TryParse(publishedText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out published))
            {
                info.PublishedAt = published;
            }

            info.Version = AppVersions.Parse(info.Tag);
            if (info.Version == null)
            {
                throw new FormatException("The release tag \"" + info.Tag + "\" is not a version number.");
            }

            var assets = root["assets"] as JArray;
            if (assets != null)
            {
                foreach (JToken token in assets)
                {
                    var asset = new ReleaseAsset
                    {
                        Name = (string)token["name"] ?? "",
                        Url = (string)token["browser_download_url"] ?? "",
                        Size = (long?)token["size"] ?? 0
                    };
                    if (asset.Name.Length > 0 && asset.Url.Length > 0)
                    {
                        info.Assets.Add(asset);
                    }
                }
            }

            info.Installer = info.Assets.Find(a => IsInstallerName(a.Name));
            if (info.Installer != null)
            {
                string hashName = info.Installer.Name + HashExtension;
                info.InstallerHash = info.Assets.Find(a => string.Equals(a.Name, hashName, StringComparison.OrdinalIgnoreCase));
            }
            return info;
        }

        public static bool IsInstallerName(string name)
        {
            return name != null && InstallerName.IsMatch(name);
        }
    }
}
