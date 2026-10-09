using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core;
using PrintVect.Core.Update;

namespace PrintVect.Tests
{
    [TestClass]
    public class AppVersionsTests
    {
        [TestMethod]
        public void ParsesTagsAndPlainVersions()
        {
            Assert.AreEqual(new Version(0, 2, 1), AppVersions.Parse("v0.2.1"));
            Assert.AreEqual(new Version(0, 2, 1), AppVersions.Parse("0.2.1.0"));
            Assert.AreEqual(new Version(1, 0, 0), AppVersions.Parse("V1.0"));
            Assert.AreEqual(new Version(1, 2, 3), AppVersions.Parse("1.2.3-beta"));
            Assert.AreEqual(new Version(1, 2, 3), AppVersions.Parse(" v1.2.3+abc "));
            Assert.IsNull(AppVersions.Parse("latest"));
            Assert.IsNull(AppVersions.Parse(""));
            Assert.IsNull(AppVersions.Parse(null));
            Assert.IsNull(AppVersions.Parse("1.2.3.4.5"));
            Assert.IsNull(AppVersions.Parse("1.x"));
        }

        [TestMethod]
        public void ComparesOnThreeNumbers()
        {
            Assert.IsTrue(AppVersions.IsNewer(new Version(0, 2, 1), new Version(0, 2, 0)));
            Assert.IsTrue(AppVersions.IsNewer(new Version(0, 10, 0), new Version(0, 9, 9)));
            Assert.IsTrue(AppVersions.IsNewer(new Version(1, 0, 0), null));
            Assert.IsFalse(AppVersions.IsNewer(new Version(1, 0, 0), new Version(1, 0, 0, 0)));
            Assert.IsFalse(AppVersions.IsNewer(new Version(1, 0), new Version(1, 0, 0)));
            Assert.IsFalse(AppVersions.IsNewer(new Version(0, 1, 9), new Version(0, 2, 0)));
            Assert.IsFalse(AppVersions.IsNewer(null, new Version(0, 2, 0)));
            Assert.AreEqual("1.0.0", AppVersions.Format(new Version(1, 0)));
            Assert.AreEqual("?", AppVersions.Format(null));
        }

        [TestMethod]
        public void CurrentIsTheProgramVersion()
        {
            Assert.AreEqual(AppVersions.Parse(AppInfo.Version), AppVersions.Current);
        }
    }

    [TestClass]
    public class ReleaseInfoTests
    {
        public static string ReleaseJson(string tag, string baseUrl, long size, bool withHash, bool draft = false)
        {
            string assets = "{ \"name\": \"PrintVect-Setup-" + tag.TrimStart('v') + ".exe\", \"size\": " + size
                            + ", \"browser_download_url\": \"" + baseUrl + "download/setup.exe\" }";
            if (withHash)
            {
                assets += ", { \"name\": \"PrintVect-Setup-" + tag.TrimStart('v') + ".exe.sha256\", \"size\": 92"
                          + ", \"browser_download_url\": \"" + baseUrl + "download/setup.exe.sha256\" }";
            }
            return "{ \"tag_name\": \"" + tag + "\", \"name\": \"PrintVect " + tag.TrimStart('v') + "\", \"draft\": " + (draft ? "true" : "false")
                   + ", \"prerelease\": false, \"html_url\": \"" + baseUrl + "tag/" + tag + "\", \"published_at\": \"2026-10-09T10:00:00Z\""
                   + ", \"body\": \"Notes\", \"assets\": [ " + assets + " ] }";
        }

        [TestMethod]
        public void ReadsVersionAndFiles()
        {
            ReleaseInfo info = ReleaseInfo.Parse(ReleaseJson("v0.2.1", "https://example.test/", 2201849, true));
            Assert.AreEqual("v0.2.1", info.Tag);
            Assert.AreEqual(new Version(0, 2, 1), info.Version);
            Assert.AreEqual("PrintVect 0.2.1", info.Title);
            Assert.AreEqual("Notes", info.Notes);
            Assert.IsFalse(info.Draft);
            Assert.IsFalse(info.PreRelease);
            Assert.AreEqual(new DateTime(2026, 10, 9, 10, 0, 0), info.PublishedAt);
            Assert.AreEqual(2, info.Assets.Count);
            Assert.IsNotNull(info.Installer);
            Assert.AreEqual("PrintVect-Setup-0.2.1.exe", info.Installer.Name);
            Assert.AreEqual(2201849L, info.Installer.Size);
            Assert.AreEqual("https://example.test/download/setup.exe", info.Installer.Url);
            Assert.IsNotNull(info.InstallerHash);
            Assert.AreEqual("PrintVect-Setup-0.2.1.exe.sha256", info.InstallerHash.Name);
        }

        [TestMethod]
        public void MissingFilesLeaveTheInstallerNull()
        {
            ReleaseInfo info = ReleaseInfo.Parse("{ \"tag_name\": \"v3.0.0\", \"assets\": [ { \"name\": \"notes.txt\", \"size\": 3, \"browser_download_url\": \"https://x/notes.txt\" } ] }");
            Assert.AreEqual(new Version(3, 0, 0), info.Version);
            Assert.IsNull(info.Installer);
            Assert.IsNull(info.InstallerHash);
            Assert.AreEqual("", info.Notes);

            ReleaseInfo noHash = ReleaseInfo.Parse(ReleaseJson("v0.3.0", "https://example.test/", 10, false));
            Assert.IsNotNull(noHash.Installer);
            Assert.IsNull(noHash.InstallerHash);
        }

        [TestMethod]
        public void RejectsNonJsonAndNonVersionTags()
        {
            Assert.ThrowsException<FormatException>(() => ReleaseInfo.Parse("<html>Not Found</html>"));
            Assert.ThrowsException<FormatException>(() => ReleaseInfo.Parse(""));
            Assert.ThrowsException<FormatException>(() => ReleaseInfo.Parse("{ \"tag_name\": \"latest\" }"));
            Assert.ThrowsException<FormatException>(() => ReleaseInfo.Parse("{ \"message\": \"Not Found\" }"));
        }

        [TestMethod]
        public void RecognisesSetupFileNames()
        {
            Assert.IsTrue(ReleaseInfo.IsInstallerName("PrintVect-Setup-0.2.1.exe"));
            Assert.IsTrue(ReleaseInfo.IsInstallerName("printvect-setup-1.0.0.EXE"));
            Assert.IsFalse(ReleaseInfo.IsInstallerName("PrintVect-Setup-0.2.1.exe.sha256"));
            Assert.IsFalse(ReleaseInfo.IsInstallerName("PrintVect-build-32.zip"));
            Assert.IsFalse(ReleaseInfo.IsInstallerName("PrintVect-Setup-.exe"));
            Assert.IsFalse(ReleaseInfo.IsInstallerName(null));
        }
    }

    [TestClass]
    public class Sha256HexTests
    {
        [TestMethod]
        public void HashesAndParsesChecksumFiles()
        {
            const string helloHash = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
            byte[] hello = Encoding.ASCII.GetBytes("hello");
            Assert.AreEqual(helloHash, Sha256Hex.OfBytes(hello));
            using (var temp = new TempFolder())
            {
                string path = temp.File("hello.bin");
                File.WriteAllBytes(path, hello);
                Assert.AreEqual(helloHash, Sha256Hex.OfFile(path));
            }
            Assert.AreEqual(helloHash, Sha256Hex.ParseHashText(helloHash.ToUpperInvariant() + "  PrintVect-Setup-0.2.1.exe\n"));
            Assert.AreEqual(helloHash, Sha256Hex.ParseHashText("SHA256: " + helloHash));
            Assert.IsNull(Sha256Hex.ParseHashText("nothing here"));
            Assert.IsNull(Sha256Hex.ParseHashText(helloHash.Substring(1)));
            Assert.IsNull(Sha256Hex.ParseHashText(null));
            Assert.IsTrue(Sha256Hex.Equal(helloHash, helloHash.ToUpperInvariant()));
        }
    }

    [TestClass]
    public class UpdateInstallerTests
    {
        [TestMethod]
        public void BuildsSilentArguments()
        {
            Assert.AreEqual("/SILENT /NORESTART", UpdateInstaller.BuildArguments(null));
            Assert.AreEqual("/SILENT /NORESTART /LOG=\"C:\\Program Data\\u.log\"", UpdateInstaller.BuildArguments("C:\\Program Data\\u.log"));
        }

        [TestMethod]
        public void NamesTheLogAfterTheVersion()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pv-updates");
            Assert.AreEqual(Path.Combine(dir, "update-0.2.1.log"), UpdateInstaller.LogPathFor(Path.Combine(dir, "PrintVect-Setup-0.2.1.exe")));
            Assert.AreEqual(Path.Combine(dir, "update-odd.log"), UpdateInstaller.LogPathFor(Path.Combine(dir, "odd.exe")));
            Assert.AreEqual(new Version(0, 2, 1), UpdateInstaller.VersionFromFileName("PrintVect-Setup-0.2.1.exe"));
            Assert.IsNull(UpdateInstaller.VersionFromFileName("other.exe"));
            Assert.IsNull(UpdateInstaller.VersionFromFileName(null));
        }

        [TestMethod]
        public void CleanUpRemovesOldSetupsAndHalfDownloads()
        {
            using (var temp = new TempFolder())
            {
                foreach (string name in new[] { "PrintVect-Setup-0.1.0.exe", "PrintVect-Setup-0.2.0.exe", "PrintVect-Setup-9.9.9.exe",
                                                "PrintVect-Setup-9.9.9.exe.part", "update-0.2.0.log" })
                {
                    File.WriteAllText(temp.File(name), name);
                }
                Assert.AreEqual(3, UpdateInstaller.CleanUp(temp.Path, new Version(0, 2, 0)));
                Assert.IsFalse(File.Exists(temp.File("PrintVect-Setup-0.1.0.exe")), "older setup");
                Assert.IsFalse(File.Exists(temp.File("PrintVect-Setup-0.2.0.exe")), "the running version's setup");
                Assert.IsFalse(File.Exists(temp.File("PrintVect-Setup-9.9.9.exe.part")), "half download");
                Assert.IsTrue(File.Exists(temp.File("PrintVect-Setup-9.9.9.exe")), "a newer setup stays");
                Assert.IsTrue(File.Exists(temp.File("update-0.2.0.log")), "the last setup log stays");
                Assert.AreEqual(0, UpdateInstaller.CleanUp(temp.File("missing"), new Version(0, 2, 0)));
            }
        }
    }

    /// <summary>A loopback stand-in for api.github.com and the release download URLs.</summary>
    internal sealed class FakeGitHub : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

        public FakeGitHub()
        {
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port + "/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            Task.Run(() => ServeAsync());
        }

        public string BaseUrl { get; }
        public string FeedUrl { get { return BaseUrl + "releases/latest"; } }
        public int FeedStatus { get; set; } = 200;
        public string FeedJson { get; set; } = "{}";
        public byte[] Installer { get; set; } = new byte[0];
        public string HashText { get; set; }
        public int Requests;

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return; // stopped
                }
                Interlocked.Increment(ref Requests);
                try
                {
                    Answer(context);
                }
                catch (Exception)
                {
                    // the client went away; nothing to do
                }
            }
        }

        private void Answer(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;
            byte[] body;
            int status = 200;
            if (path == "/releases/latest")
            {
                status = FeedStatus;
                body = Encoding.UTF8.GetBytes(status == 200 ? FeedJson : "{ \"message\": \"Not Found\" }");
            }
            else if (path == "/download/setup.exe")
            {
                body = Installer;
            }
            else if (path == "/download/setup.exe.sha256" && HashText != null)
            {
                body = Encoding.ASCII.GetBytes(HashText);
            }
            else
            {
                status = 404;
                body = Encoding.UTF8.GetBytes("not found");
            }
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            _stopping.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _listener.Close(); } catch (Exception) { }
        }
    }

    [TestClass]
    public class UpdateCheckerTests
    {
        private static readonly Version Current = new Version(0, 2, 0);

        private static byte[] SampleInstaller()
        {
            var bytes = new byte[100 * 1024 + 17];
            new Random(42).NextBytes(bytes);
            return bytes;
        }

        [TestMethod]
        public async Task FindsANewerRelease()
        {
            using (var github = new FakeGitHub())
            {
                github.FeedJson = ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, 123, true);
                UpdateCheckResult result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.UpdateAvailable, result.Outcome, result.Error);
                Assert.IsTrue(result.IsNewer);
                Assert.AreEqual(new Version(9, 9, 9), result.Release.Version);
                Assert.AreEqual("PrintVect-Setup-9.9.9.exe", result.Release.Installer.Name);
                Assert.AreEqual(Current, result.Current);
            }
        }

        [TestMethod]
        public async Task SaysUpToDateWhenNothingIsNewer()
        {
            using (var github = new FakeGitHub())
            {
                github.FeedJson = ReleaseInfoTests.ReleaseJson("v0.2.0", github.BaseUrl, 123, true);
                UpdateCheckResult result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.UpToDate, result.Outcome, result.Error);
                Assert.IsFalse(result.IsNewer);
                Assert.AreEqual("v0.2.0", result.Release.Tag);

                github.FeedJson = ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, 123, true, draft: true);
                result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.UpToDate, result.Outcome, "a draft is not an update");
            }
        }

        [TestMethod]
        public async Task NoReleaseYetCountsAsUpToDate()
        {
            using (var github = new FakeGitHub())
            {
                github.FeedStatus = 404;
                UpdateCheckResult result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.UpToDate, result.Outcome, result.Error);
                Assert.IsNull(result.Release);
            }
        }

        [TestMethod]
        public async Task ProblemsAreReportedInPlainLanguage()
        {
            using (var github = new FakeGitHub())
            {
                github.FeedStatus = 500;
                UpdateCheckResult result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.Failed, result.Outcome);
                StringAssert.Contains(result.Error, "500");

                github.FeedStatus = 403;
                result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.Failed, result.Outcome);
                StringAssert.Contains(result.Error, "limiting");

                github.FeedStatus = 200;
                github.FeedJson = "<html>maintenance</html>";
                result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.Failed, result.Outcome);
                StringAssert.Contains(result.Error, "did not understand");

                github.FeedJson = "{ \"tag_name\": \"v9.9.9\", \"assets\": [] }";
                result = await new UpdateChecker(github.FeedUrl, Current).CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.Failed, result.Outcome);
                StringAssert.Contains(result.Error, "no PrintVect-Setup file");
            }

            // Nothing listens on port 1: the kind of failure a PC without internet produces.
            UpdateCheckResult offline = await new UpdateChecker("http://127.0.0.1:1/releases/latest", Current).CheckAsync(CancellationToken.None);
            Assert.AreEqual(UpdateCheckOutcome.Failed, offline.Outcome);
            Assert.IsFalse(string.IsNullOrEmpty(offline.Error));
        }

        [TestMethod]
        public async Task DownloadsAndVerifiesTheSetupProgram()
        {
            byte[] installer = SampleInstaller();
            using (var github = new FakeGitHub())
            using (var temp = new TempFolder())
            {
                github.Installer = installer;
                github.HashText = Sha256Hex.OfBytes(installer) + "  PrintVect-Setup-9.9.9.exe\n";
                github.FeedJson = ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, installer.Length, true);
                var checker = new UpdateChecker(github.FeedUrl, Current);
                UpdateCheckResult result = await checker.CheckAsync(CancellationToken.None);
                Assert.AreEqual(UpdateCheckOutcome.UpdateAvailable, result.Outcome, result.Error);

                string folder = temp.File("updates");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "PrintVect-Setup-0.1.0.exe"), "old");
                var seen = new List<DownloadProgress>();
                var progress = new SynchronousProgress(seen.Add);

                string path = await checker.DownloadAsync(result.Release, folder, progress, CancellationToken.None);
                Assert.AreEqual(Path.Combine(folder, "PrintVect-Setup-9.9.9.exe"), path);
                CollectionAssert.AreEqual(installer, File.ReadAllBytes(path));
                Assert.IsFalse(File.Exists(path + UpdateChecker.PartExtension));
                Assert.IsFalse(File.Exists(Path.Combine(folder, "PrintVect-Setup-0.1.0.exe")), "older downloads are removed");
                Assert.IsTrue(seen.Count > 0, "progress was reported");
                Assert.AreEqual(installer.Length, seen[seen.Count - 1].Done);
                Assert.AreEqual(installer.Length, seen[seen.Count - 1].Total);
            }
        }

        [TestMethod]
        public async Task RejectsADamagedOrIncompleteDownload()
        {
            byte[] installer = SampleInstaller();
            using (var github = new FakeGitHub())
            using (var temp = new TempFolder())
            {
                github.Installer = installer;
                github.HashText = Sha256Hex.OfBytes(new byte[] { 1, 2, 3 }) + "  PrintVect-Setup-9.9.9.exe\n";
                var checker = new UpdateChecker(github.FeedUrl, Current);

                ReleaseInfo wrongHash = ReleaseInfo.Parse(ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, installer.Length, true));
                UpdateVerificationException damaged = await Assert.ThrowsExceptionAsync<UpdateVerificationException>(
                    () => checker.DownloadAsync(wrongHash, temp.Path, null, CancellationToken.None));
                StringAssert.Contains(damaged.Message, "checksum");
                Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length, "nothing is left behind");

                ReleaseInfo wrongSize = ReleaseInfo.Parse(ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, 5, false));
                UpdateVerificationException incomplete = await Assert.ThrowsExceptionAsync<UpdateVerificationException>(
                    () => checker.DownloadAsync(wrongSize, temp.Path, null, CancellationToken.None));
                StringAssert.Contains(incomplete.Message, "incomplete");
                Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length);

                // Without a checksum file the size is all that can be checked, and it matches here.
                ReleaseInfo noHash = ReleaseInfo.Parse(ReleaseInfoTests.ReleaseJson("v9.9.9", github.BaseUrl, installer.Length, false));
                string path = await checker.DownloadAsync(noHash, temp.Path, null, CancellationToken.None);
                CollectionAssert.AreEqual(installer, File.ReadAllBytes(path));
            }
        }

        /// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; needs a message loop).</summary>
        private sealed class SynchronousProgress : IProgress<DownloadProgress>
        {
            private readonly Action<DownloadProgress> _report;
            public SynchronousProgress(Action<DownloadProgress> report) { _report = report; }
            public void Report(DownloadProgress value) { _report(value); }
        }
    }
}
