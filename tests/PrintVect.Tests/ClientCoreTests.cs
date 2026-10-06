using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Elevation;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class XpsFormatSnifferTests
    {
        [TestMethod]
        public void RecognisesXpsAndReadsTheTitle()
        {
            using (var temp = new TempFolder())
            {
                string path = XpsPackages.Write(temp.File("a.xps"), false, "Letter to RMS");
                XpsFileInfo info = XpsFormatSniffer.Inspect(path);
                Assert.AreEqual(JobFormats.Xps, info.Format);
                Assert.AreEqual("Letter to RMS", info.Title);
                Assert.IsTrue(info.IsXpsPackage);
                Assert.IsNull(info.Problem);
            }
        }

        [TestMethod]
        public void RecognisesOpenXpsWhateverTheFileIsCalled()
        {
            using (var temp = new TempFolder())
            {
                string path = XpsPackages.Write(temp.File("job.xps"), true, null);
                XpsFileInfo info = XpsFormatSniffer.Inspect(path);
                Assert.AreEqual(JobFormats.Oxps, info.Format);
                Assert.IsNull(info.Title);
                Assert.AreEqual("oxps", XpsFormatSniffer.ExtensionFor(info.Format));
            }
        }

        [TestMethod]
        public void ReadsTheCopiesCountFromThePrintTicket()
        {
            using (var temp = new TempFolder())
            {
                XpsFileInfo two = XpsFormatSniffer.Inspect(XpsPackages.Write(temp.File("two.oxps"), true, "Receipt", 0, 2));
                Assert.AreEqual(2, two.Copies);
                Assert.AreEqual(JobFormats.Oxps, two.Format);
                Assert.AreEqual("Receipt", two.Title);

                XpsFileInfo none = XpsFormatSniffer.Inspect(XpsPackages.Write(temp.File("one.xps"), false, null));
                Assert.AreEqual(1, none.Copies);

                XpsFileInfo many = XpsFormatSniffer.Inspect(XpsPackages.Write(temp.File("many.xps"), false, null, 0, 500));
                Assert.AreEqual(XpsFormatSniffer.MaxCopies, many.Copies, "the count is capped");
            }
        }

        [TestMethod]
        public void PrintTicketPartsAreResolvedRelativeToTheirPart()
        {
            string rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                          + "<Relationship Id=\"a\" Type=\"http://schemas.microsoft.com/xps/2005/06/printticket\" Target=\"../Metadata/Doc_PT.xml\" />"
                          + "<Relationship Id=\"b\" Type=\"http://schemas.microsoft.com/xps/2005/06/required-resource\" Target=\"/Resources/f.ttf\" />"
                          + "<Relationship Id=\"c\" Type=\"http://schemas.openxps.org/oxps/v1.0/printticket\" Target=\"/Metadata/Job_PT.xml\" /></Relationships>";
            var targets = new System.Collections.Generic.List<string>(XpsFormatSniffer.PrintTicketTargets("Documents/1/_rels/FixedDocument.fdoc.rels", rels));
            CollectionAssert.AreEqual(new[] { "Documents/Metadata/Doc_PT.xml", "Metadata/Job_PT.xml" }, targets);
            Assert.AreEqual("Metadata/Job_PT.xml", XpsFormatSniffer.ResolvePartName("", "/Metadata/Job_PT.xml"));
            Assert.AreEqual(3, XpsFormatSniffer.CopiesFrom("<t xmlns:psf=\"x\"><psf:ParameterInit name=\"ns0000:JobCopiesAllDocuments\"><psf:Value>3</psf:Value></psf:ParameterInit></t>"));
            Assert.AreEqual(1, XpsFormatSniffer.CopiesFrom("<t><ParameterInit name=\"psk:PageMediaSize\"><Value>2</Value></ParameterInit></t>"));
            Assert.AreEqual(1, XpsFormatSniffer.CopiesFrom("not xml"));
        }

        [TestMethod]
        public void RejectsFilesThatAreNotPackages()
        {
            using (var temp = new TempFolder())
            {
                string text = temp.File("notes.xps");
                File.WriteAllText(text, "hello");
                XpsFileInfo info = XpsFormatSniffer.Inspect(text);
                Assert.IsFalse(info.IsXpsPackage);
                Assert.IsNotNull(info.Problem);

                XpsFileInfo missing = XpsFormatSniffer.Inspect(temp.File("missing.xps"));
                Assert.IsFalse(missing.IsXpsPackage);
                Assert.IsNotNull(missing.Problem);
            }
        }

        [TestMethod]
        public void TheShippedTestPageIsXps()
        {
            string sample = FindSample("PrintVect-test-page.xps");
            if (sample == null)
            {
                Assert.Inconclusive("docs/samples not found next to the test binaries");
            }
            XpsFileInfo info = XpsFormatSniffer.Inspect(sample);
            Assert.AreEqual(JobFormats.Xps, info.Format);
        }

        private static string FindSample(string name)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "docs", "samples", name);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }

    [TestClass]
    public class ClientPrinterNamesTests
    {
        [TestMethod]
        public void BuildsTheWindowsPrinterName()
        {
            Assert.AreEqual("PrintVect - Counter 1 Laser @COUNTER1", ClientPrinterNames.PrinterName("Counter 1 Laser", "COUNTER1"));
            Assert.AreEqual("PrintVect - Printer @SPM", ClientPrinterNames.PrinterName("", "SPM"));
            Assert.AreEqual("PrintVect - HP Laser", ClientPrinterNames.PrinterName("HP Laser", null));
            Assert.IsTrue(ClientPrinterNames.IsPrintVectPrinter("PrintVect - X @Y"));
            Assert.IsFalse(ClientPrinterNames.IsPrintVectPrinter("HP Laser 103 107 108"));
        }

        [TestMethod]
        public void DropsCharactersWindowsRejects()
        {
            string name = ClientPrinterNames.PrinterName("Mail\\Branch, Counter \"1\"", "HOST!PC");
            Assert.IsFalse(name.Contains("\\"), name);
            Assert.IsFalse(name.Contains(","), name);
            Assert.IsFalse(name.Contains("\""), name);
            Assert.AreEqual("PrintVect - Mail Branch Counter 1 @HOST PC", name);
        }

        [TestMethod]
        public void LongNamesAreCut()
        {
            string name = ClientPrinterNames.PrinterName(new string('a', 200), "HOST");
            Assert.AreEqual(ClientPrinterNames.MaxPrinterNameLength, name.Length);
        }

        [TestMethod]
        public void SpoolFolderIsUnderTheSpoolDirectory()
        {
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "pv-names"));
            string folder = ClientPrinterNames.SpoolFolder(paths, "0afbc594d5975d71");
            Assert.AreEqual(Path.Combine(paths.SpoolDir, "0afbc594d5975d71"), folder);
            Assert.AreEqual(Path.Combine(folder, "job.xps"), ClientPrinterNames.PortFile(paths, "0afbc594d5975d71"));
            Assert.AreEqual("abc", ClientPrinterNames.SafeId("a/b\\c"));
            Assert.ThrowsException<ArgumentException>(() => ClientPrinterNames.SafeId("../"));
        }
    }

    [TestClass]
    public class ElevateArgumentsTests
    {
        [TestMethod]
        public void QuotesValuesWithSpacesAndTrailingBackslashes()
        {
            Assert.AreEqual("plain", ElevateArguments.Quote("plain"));
            Assert.AreEqual("\"a b\"", ElevateArguments.Quote("a b"));
            Assert.AreEqual("C:\\dir\\", ElevateArguments.Quote("C:\\dir\\"), "no spaces: no quotes needed, a trailing backslash is literal");
            Assert.AreEqual("\"C:\\my dir\\\\\"", ElevateArguments.Quote("C:\\my dir\\"), "quoted: the trailing backslash is doubled");
            Assert.AreEqual("\"say \\\"hi\\\"\"", ElevateArguments.Quote("say \"hi\""));
            Assert.AreEqual("\"\"", ElevateArguments.Quote(""));
        }

        [TestMethod]
        public void BuildsAndParsesACommandLine()
        {
            var args = new ElevateArguments { Command = ElevateCommands.AddPrinter };
            args.Options[ElevateCommands.OptionId] = "0afbc594d5975d71";
            args.Options[ElevateCommands.OptionName] = "PrintVect - Mail Branch @DESKTOP-JK66OQV";
            args.Options[ElevateCommands.OptionPort] = "C:\\ProgramData\\PrintVect\\spool\\0afbc594d5975d71\\job.xps";

            string line = args.ToCommandLine();
            Assert.AreEqual("add-printer /id 0afbc594d5975d71 /name \"PrintVect - Mail Branch @DESKTOP-JK66OQV\" /port C:\\ProgramData\\PrintVect\\spool\\0afbc594d5975d71\\job.xps", line);

            ElevateArguments parsed = ElevateArguments.Parse(new[]
            {
                "Add-Printer", "/id", "0afbc594d5975d71", "/name", "PrintVect - Mail Branch @DESKTOP-JK66OQV", "-result", "C:\\r.json"
            });
            Assert.AreEqual(ElevateCommands.AddPrinter, parsed.Command);
            Assert.AreEqual("0afbc594d5975d71", parsed.Get(ElevateCommands.OptionId));
            Assert.AreEqual("PrintVect - Mail Branch @DESKTOP-JK66OQV", parsed.Get("NAME"));
            Assert.AreEqual("C:\\r.json", parsed.Get(ElevateCommands.OptionResult));
            Assert.IsNull(parsed.Get(ElevateCommands.OptionPort));
        }

        [TestMethod]
        public void ParsesNothingGracefully()
        {
            ElevateArguments parsed = ElevateArguments.Parse(new string[0]);
            Assert.IsNull(parsed.Command);
            Assert.AreEqual("", new ElevateArguments().ToCommandLine());
        }
    }
}
