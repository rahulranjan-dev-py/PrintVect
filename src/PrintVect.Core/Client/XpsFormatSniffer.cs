using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Copies asked for in the package's print ticket (JobCopiesAllDocuments); 1 when none.</summary>
        public int Copies { get; set; } = 1;

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
    /// stores and the copies count from the print ticket, which the XPS writer records but the
    /// host's spooler path would otherwise ignore.
    /// </summary>
    public static class XpsFormatSniffer
    {
        private const string XpsNamespace = "schemas.microsoft.com/xps/2005/06";
        private const string OpenXpsNamespace = "schemas.openxps.org/oxps/v1.0";
        private const string PrintTicketRelationshipSuffix = "/printticket";
        public const int MaxCopies = 99;
        private const int MaxPartChars = 256 * 1024;

        /// <summary>Never throws: an unreadable or foreign file comes back with Format null and a Problem.</summary>
        public static XpsFileInfo Inspect(string path)
        {
            var info = new XpsFileInfo();
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read, false))
                {
                    var ticketParts = new List<string>();
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName;
                        if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                        {
                            string text = ReadText(entry);
                            string format = FormatFromMarkup(text);
                            if (format != null && info.Format == null) info.Format = format;
                            ticketParts.AddRange(PrintTicketTargets(name, text));
                        }
                        else if (name.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase))
                        {
                            string format = FormatFromMarkup(ReadText(entry));
                            if (format != null && info.Format == null) info.Format = format;
                        }
                        else if (name.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase))
                        {
                            info.Title = TitleFrom(ReadText(entry));
                        }
                    }

                    foreach (string part in ticketParts.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ZipArchiveEntry ticket = zip.GetEntry(part);
                        if (ticket == null) continue;
                        int copies = CopiesFrom(ReadText(ticket));
                        if (copies > info.Copies) info.Copies = Math.Min(copies, MaxCopies);
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

        /// <summary>The package parts a .rels file points at with a print-ticket relationship.</summary>
        public static IEnumerable<string> PrintTicketTargets(string relsName, string xml)
        {
            var targets = new List<string>();
            if (string.IsNullOrEmpty(xml) || xml.IndexOf(PrintTicketRelationshipSuffix, StringComparison.OrdinalIgnoreCase) < 0) return targets;
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (Exception)
            {
                return targets;
            }
            // "A/B/_rels/C.rels" describes part "A/B/C": relative targets resolve against "A/B/".
            string relsDir = relsName.Contains("/") ? relsName.Substring(0, relsName.LastIndexOf('/') + 1) : "";
            string baseDir = relsDir.EndsWith("_rels/", StringComparison.OrdinalIgnoreCase) ? relsDir.Substring(0, relsDir.Length - "_rels/".Length) : relsDir;
            foreach (XElement rel in doc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                string type = (string)rel.Attribute("Type") ?? "";
                string target = (string)rel.Attribute("Target") ?? "";
                if (!type.EndsWith(PrintTicketRelationshipSuffix, StringComparison.OrdinalIgnoreCase) || target.Length == 0) continue;
                targets.Add(ResolvePartName(baseDir, target));
            }
            return targets;
        }

        public static string ResolvePartName(string baseDir, string target)
        {
            string combined = target.StartsWith("/", StringComparison.Ordinal) ? target.Substring(1) : baseDir + target;
            var parts = new List<string>();
            foreach (string piece in combined.Split('/'))
            {
                if (piece.Length == 0 || piece == ".") continue;
                if (piece == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(piece);
            }
            return string.Join("/", parts);
        }

        /// <summary>JobCopiesAllDocuments or DocumentCopiesAllPages from a print ticket; 1 when absent.</summary>
        public static int CopiesFrom(string ticketXml)
        {
            if (string.IsNullOrEmpty(ticketXml) || ticketXml.IndexOf("Copies", StringComparison.OrdinalIgnoreCase) < 0) return 1;
            try
            {
                XDocument doc = XDocument.Parse(ticketXml);
                int best = 1;
                foreach (XElement init in doc.Descendants().Where(e => e.Name.LocalName == "ParameterInit"))
                {
                    string name = (string)init.Attribute("name") ?? "";
                    string local = name.Contains(":") ? name.Substring(name.LastIndexOf(':') + 1) : name;
                    if (local != "JobCopiesAllDocuments" && local != "DocumentCopiesAllPages") continue;
                    XElement value = init.Elements().FirstOrDefault(e => e.Name.LocalName == "Value");
                    int copies;
                    if (value != null && int.TryParse(value.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out copies) && copies > best)
                    {
                        best = copies;
                    }
                }
                return best;
            }
            catch (Exception)
            {
                return 1;
            }
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
