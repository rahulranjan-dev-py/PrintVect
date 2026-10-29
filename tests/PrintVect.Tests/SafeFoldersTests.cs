using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;

namespace PrintVect.Tests
{
    [TestClass]
    public class SafeFoldersTests
    {
        /// <summary>A junction on Windows (no administrator rights needed), a symbolic link elsewhere.</summary>
        private static void MakeLink(string link, string target)
        {
            ProcessStartInfo info = WindowsInfo.IsWindows
                ? new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                : new ProcessStartInfo("ln", "-s \"" + target + "\" \"" + link + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            using (Process process = Process.Start(info))
            {
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                Assert.AreEqual(0, process.ExitCode, "the link could not be made");
            }
        }

        [TestMethod]
        public void FindsALinkAnywhereOnTheWay()
        {
            using (var temp = new TempFolder())
            {
                string top = temp.Path;
                string elsewhere = Directory.CreateDirectory(Path.Combine(top, "Windows")).FullName;
                string spool = Directory.CreateDirectory(Path.Combine(top, "PrintVect", "spool")).FullName;
                Directory.CreateDirectory(Path.Combine(spool, "0afbc594d5975d71"));
                MakeLink(Path.Combine(spool, "planted"), elsewhere);

                Assert.IsNull(SafeFolders.FirstLink(top, Path.Combine(spool, "0afbc594d5975d71")));
                Assert.IsNull(SafeFolders.FirstLink(top, Path.Combine(spool, "not-there-yet", "job.xps")), "missing folders are not links");
                Assert.AreEqual(Path.Combine(spool, "planted"), SafeFolders.FirstLink(top, Path.Combine(spool, "planted")));
                Assert.AreEqual(Path.Combine(spool, "planted"), SafeFolders.FirstLink(top, Path.Combine(spool, "planted", "deeper", "job.xps")));
            }
        }

        [TestMethod]
        public void CreatesNothingThroughALinkedParent()
        {
            using (var temp = new TempFolder())
            {
                string top = temp.Path;
                string elsewhere = Directory.CreateDirectory(Path.Combine(top, "System32")).FullName;
                Directory.CreateDirectory(Path.Combine(top, "PrintVect"));
                MakeLink(Path.Combine(top, "PrintVect", "spool"), elsewhere);   // spool itself replaced by a link

                Assert.ThrowsException<LinkFoundException>(() => SafeFolders.CreateFolder(top, Path.Combine(top, "PrintVect", "spool", "abc")));
                Assert.AreEqual(0, Directory.GetDirectories(elsewhere).Length, "nothing was made where the link leads");
            }
        }

        [TestMethod]
        public void GrantsUsersModifyOnTheRealFolder_AndRefusesALink()
        {
            if (!WindowsInfo.IsWindows)
            {
                Assert.Inconclusive("folder rights and junctions are Windows features");
            }
            using (var temp = new TempFolder())
            {
                string top = temp.Path;
                string elsewhere = Directory.CreateDirectory(Path.Combine(top, "System32")).FullName;
                string spool = Directory.CreateDirectory(Path.Combine(top, "PrintVect", "spool")).FullName;
                string real = Path.Combine(spool, "0afbc594d5975d71");
                string planted = Path.Combine(spool, "1bbbbbbbbbbbbbbb");
                MakeLink(planted, elsewhere);

                SafeFolders.GrantUsersModify(top, real);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                AuthorizationRuleCollection rules = Directory.GetAccessControl(real).GetAccessRules(true, false, typeof(SecurityIdentifier));
                Assert.IsTrue(rules.OfType<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(users)
                    && (r.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify && r.AccessControlType == AccessControlType.Allow));

                Assert.ThrowsException<LinkFoundException>(() => SafeFolders.GrantUsersModify(top, planted));
                AuthorizationRuleCollection after = Directory.GetAccessControl(elsewhere).GetAccessRules(true, false, typeof(SecurityIdentifier));
                Assert.IsFalse(after.OfType<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(users)), "the folder behind the link is untouched");
            }
        }
    }
}
