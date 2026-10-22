using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Client;
using PrintVect.Core.Printing;

namespace PrintVect.Tests
{
    [TestClass]
    public class PrinterKindsTests
    {
        [TestMethod]
        public void TellsTheKindFromNamePortAndDriver()
        {
            Assert.AreEqual(PrinterKind.Pdf, PrinterKinds.Classify("Microsoft Print to PDF", "PORTPROMPT:", "Microsoft Print To PDF"));
            Assert.AreEqual(PrinterKind.Xps, PrinterKinds.Classify("Microsoft XPS Document Writer", "PORTPROMPT:", "Microsoft XPS Document Writer v4"));
            Assert.AreEqual(PrinterKind.OneNote, PrinterKinds.Classify("OneNote (Desktop)", "nul:", "Send to Microsoft OneNote 16 Driver"));
            Assert.AreEqual(PrinterKind.Fax, PrinterKinds.Classify("Fax", "SHRFAX:", "Microsoft Shared Fax Driver"));
            Assert.AreEqual(PrinterKind.Usb, PrinterKinds.Classify("HP Laser 103 107 108", "USB001", "HP Laser 103 107 108"));
            Assert.AreEqual(PrinterKind.Network, PrinterKinds.Classify("Canon office", "192.168.1.20", "Canon Generic Plus UFR II"));
            Assert.AreEqual(PrinterKind.Network, PrinterKinds.Classify("Brother", "IP_192.168.10.40", "Brother HL"));
            Assert.AreEqual(PrinterKind.Network, PrinterKinds.Classify("Epson", "WSD-0a1b2c3d-0001", "EPSON L3150"));
            Assert.AreEqual(PrinterKind.Parallel, PrinterKinds.Classify("Dot matrix", "LPT1:", "Epson LQ"));
            Assert.AreEqual(PrinterKind.SavesFile, PrinterKinds.Classify("Print to file", "FILE:", "Generic / Text Only"));
            Assert.AreEqual(PrinterKind.WindowsShare, PrinterKinds.Classify(@"\\FRONT-DESK\HP LaserJet", "", ""));
            Assert.AreEqual(PrinterKind.Other, PrinterKinds.Classify("Mystery", null, null));
            Assert.AreEqual("FRONT-DESK", PrinterKinds.ShareServer(@"\\FRONT-DESK\HP LaserJet"));
            Assert.IsNull(PrinterKinds.ShareServer("HP LaserJet"));
        }

        [TestMethod]
        public void PrintVectsOwnPrintersAreRecognised()
        {
            Assert.IsTrue(ClientPrinterNames.IsPrintVectPrinter("PrintVect - Dispatch @COUNTER-1"));
            Assert.IsFalse(ClientPrinterNames.IsPrintVectPrinter("HP Laser 103 107 108"));
        }
    }
}
