using System.IO;
using System.IO.Compression;
using System.Text;

namespace PrintVect.Tests
{
    /// <summary>Builds tiny XPS / OpenXPS packages for tests (only the parts the sniffer looks at).</summary>
    internal static class XpsPackages
    {
        public const string XpsNamespace = "http://schemas.microsoft.com/xps/2005/06";
        public const string OpenXpsNamespace = "http://schemas.openxps.org/oxps/v1.0";

        public static string Write(string path, bool openXps, string title, int paddingBytes = 0)
        {
            string ns = openXps ? OpenXpsNamespace : XpsNamespace;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, false))
            {
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />"
                    + "<Default Extension=\"fdseq\" ContentType=\"application/vnd.ms-package.xps-fixeddocumentsequence+xml\" /></Types>");
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"" + ns + "/fixedrepresentation\" Target=\"/FixedDocumentSequence.fdseq\" />"
                    + (title == null ? "" : "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"/docProps/core.xml\" />")
                    + "</Relationships>");
                Add(zip, "FixedDocumentSequence.fdseq",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><FixedDocumentSequence xmlns=\"" + ns + "\"><DocumentReference Source=\"/Documents/1/FixedDocument.fdoc\" /></FixedDocumentSequence>");
                if (paddingBytes > 0)
                {
                    // Random, uncompressible bytes so transfer tests can ask for a file of roughly this size.
                    var padding = new byte[paddingBytes];
                    new System.Random(7).NextBytes(padding);
                    ZipArchiveEntry entry = zip.CreateEntry("Resources/padding.bin", CompressionLevel.NoCompression);
                    using (Stream stream = entry.Open())
                    {
                        stream.Write(padding, 0, padding.Length);
                    }
                }
                if (title != null)
                {
                    Add(zip, "docProps/core.xml",
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?><coreProperties xmlns=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" "
                        + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>" + System.Security.SecurityElement.Escape(title) + "</dc:title><dc:creator>tester</dc:creator></coreProperties>");
                }
            }
            return path;
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using (Stream stream = entry.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
