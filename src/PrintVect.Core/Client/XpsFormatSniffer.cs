using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Client
{
    /// <summary>What a look inside a print file found.</summary>
    public sealed class XpsFileInfo
    {
        /// <summary>"xps", "oxps", or null when the file is not an XPS package at all.</summary>
        public string Format { get; set; }

        /// <summary>The document title the XPS writer stored (docProps/core.xml), or null.</summary>
        public string Title { get; set; }

        /// <summary>Why Format is null, in plain words.</summary>
        public string Problem { get; set; }

        public bool IsXpsPackage
        {
            get { return Format != null; }
        }
    }

    /// <summary>
    /// Tells an XPS file from an OpenXPS file by looking inside the package rather than at the file
    /// name: the Windows 8+ XPS Document Writer writes OpenXPS whatever the port file is called, and
    /// the host must know which one it got (brief 11.2). Also reads the document title the writer
    /// stores, so the host's job list can show "Letter to RMS" instead of "job".
    /// </summary>
    public static class XpsFormatSniffer
    {
        private const string XpsNamespace = "schemas.microsoft.com/xps/2005/06";
        private const string OpenXpsNamespace = "schemas.openxps.org/oxps/v1.0";
        private const int MaxPartChars = 64 * 1024;

        /// <summary>Never throws: an unreadable or foreign file comes back with Format null and a Problem.</summary>
        public static XpsFileInfo Inspect(string path)
        {
            var info = new XpsFileInfo();
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read, false))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName;
                        if (name.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase))
                        {
                            string format = FormatFromMarkup(ReadText(entry));
                            if (format != null && info.Format == null)
                            {
                                info.Format = format;
                            }
                        }
                        else if (name.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase))
                        {
                            info.Title = TitleFrom(ReadText(entry));
                        }
                    }
                }
                if (info.Format == null)
                {
                    info.Problem = "The file is a ZIP package but not an XPS or OpenXPS document.";
                }
            }
            catch (InvalidDataException)
            {
                info.Problem = "The file is not an XPS package (not a ZIP file).";
            }
            catch (Exception ex)
            {
                info.Problem = "The file could not be read: " + ex.Message;
            }
            return info;
        }

        /// <summary>The file extension PrintVect uses for a format: "xps" or "oxps".</summary>
        public static string ExtensionFor(string format)
        {
            return format == JobFormats.Oxps ? JobFormats.Oxps : JobFormats.Xps;
        }

        private static string FormatFromMarkup(string text)
        {
            if (text == null) return null;
            if (text.IndexOf(OpenXpsNamespace, StringComparison.OrdinalIgnoreCase) >= 0) return JobFormats.Oxps;
            if (text.IndexOf(XpsNamespace, StringComparison.OrdinalIgnoreCase) >= 0) return JobFormats.Xps;
            return null;
        }

        private static string TitleFrom(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            try
            {
                XDocument doc = XDocument.Parse(xml);
                XElement title = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "title");
                string value = title == null ? null : title.Value.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadText(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var buffer = new char[MaxPartChars];
                int read = reader.Read(buffer, 0, buffer.Length);
                return read <= 0 ? "" : new string(buffer, 0, read);
            }
        }
    }
}
