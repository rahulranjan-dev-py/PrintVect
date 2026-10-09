using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Update
{
    public enum UpdateCheckOutcome
    {
        UpToDate,
        UpdateAvailable,
        Failed
    }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckOutcome Outcome { get; set; }
        public DateTime CheckedAt { get; set; }
        public Version Current { get; set; }

        /// <summary>The latest release GitHub reported; null when there is none yet or the check failed.</summary>
        public ReleaseInfo Release { get; set; }

        /// <summary>Plain-language reason when Outcome is Failed.</summary>
        public string Error { get; set; }

        public bool IsNewer
        {
            get { return Outcome == UpdateCheckOutcome.UpdateAvailable && Release != null; }
        }
    }

    public sealed class DownloadProgress
    {
        public DownloadProgress(long done, long total)
        {
            Done = done;
            Total = total;
        }

        public long Done { get; }

        /// <summary>Bytes expected, or 0 when unknown.</summary>
        public long Total { get; }
    }

    /// <summary>Thrown when the downloaded setup program is not what the release promised.</summary>
    public sealed class UpdateVerificationException : Exception
    {
        public UpdateVerificationException(string message) : base(message) { }
    }

    /// <summary>
    /// The one place PrintVect talks to the internet (CLAUDE.md): it asks GitHub for this
    /// repository's latest release, compares its version with the running program, and can download
    /// the setup program into the updates folder and verify its size and SHA-256 before the app
    /// runs it. Printing and discovery never leave the office network.
    /// </summary>
    public sealed class UpdateChecker
    {
        public const string DefaultFeedUrl = "https://api.github.com/repos/rahulranjan-dev-py/PrintVect/releases/latest";
        public const string PartExtension = ".part";
        public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
        private const int BufferSize = 64 * 1024;

        public UpdateChecker(string feedUrl, Version current)
        {
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                throw new ArgumentException("A feed URL is required.", nameof(feedUrl));
            }
            FeedUrl = feedUrl;
            Current = AppVersions.Normalize(current) ?? new Version(0, 0, 0);
        }

        public string FeedUrl { get; }
        public Version Current { get; }

        /// <summary>Asks for the latest release. Never throws except when <paramref name="ct"/> is cancelled.</summary>
        public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
        {
            var result = new UpdateCheckResult { CheckedAt = DateTime.Now, Current = Current };
            Log.Info("Update check: asking " + FeedUrl + " (this PC runs " + AppVersions.Format(Current) + ").");
            try
            {
                using (HttpClient client = CreateClient(CheckTimeout))
                using (HttpResponseMessage response = await client.GetAsync(FeedUrl, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Log.Info("Update check: no release has been published yet (404); nothing to update to.");
                        result.Outcome = UpdateCheckOutcome.UpToDate;
                        return result;
                    }
                    if (status == 403 || status == 429)
                    {
                        return Fail(result, "github.com is limiting update checks from this network for a while (answer "
                                            + status + "); the next check will try again");
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return Fail(result, "github.com answered " + status + " " + response.ReasonPhrase);
                    }

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ReleaseInfo release = ReleaseInfo.Parse(json);
                    result.Release = release;
                    if (release.Draft || release.PreRelease)
                    {
                        Log.Info("Update check: the latest release " + release.Tag + " is a draft or pre-release; ignored.");
                        result.Outcome = UpdateCheckOutcome.UpToDate;
                        return result;
                    }
                    if (!AppVersions.IsNewer(release.Version, Current))
                    {
                        Log.Info("Update check: the latest release is " + release.Tag + "; this PC is up to date.");
                        result.Outcome = UpdateCheckOutcome.UpToDate;
                        return result;
                    }
                    if (release.Installer == null)
                    {
                        return Fail(result, "release " + release.Tag + " on github.com has no PrintVect-Setup file attached");
                    }
                    Log.Info("Update check: " + AppVersions.Format(release.Version) + " is available (" + release.Installer.Name
                             + ", " + release.Installer.Size + " bytes" + (release.InstallerHash == null ? ", no checksum file" : ", checksum file present") + ").");
                    result.Outcome = UpdateCheckOutcome.UpdateAvailable;
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }
                Log.Error("Update check failed.", ex);
                return Fail(result, DescribeError(ex));
            }
        }

        /// <summary>
        /// Downloads the release's setup program into <paramref name="folder"/>, checks its size
        /// and (when the release carries one) its SHA-256, and returns the verified file's path.
        /// Older files in the folder are removed first. Throws on any failure; nothing half-done is left behind.
        /// </summary>
        public async Task<string> DownloadAsync(ReleaseInfo release, string folder, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            if (release == null || release.Installer == null)
            {
                throw new ArgumentException("The release has no setup program.", nameof(release));
            }
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, SafeFileName(release.Installer.Name));
            string part = target + PartExtension;
            RemoveOtherFiles(folder, Path.GetFileName(target));
            Log.Info("Update: downloading " + release.Installer.Url + " to " + target + ".");

            try
            {
                using (HttpClient client = CreateClient(DownloadTimeout))
                {
                    string expectedHash = null;
                    if (release.InstallerHash != null)
                    {
                        using (HttpResponseMessage hashResponse = await client.GetAsync(release.InstallerHash.Url, ct).ConfigureAwait(false))
                        {
                            if (!hashResponse.IsSuccessStatusCode)
                            {
                                throw new HttpRequestException("github.com answered " + (int)hashResponse.StatusCode + " "
                                                               + hashResponse.ReasonPhrase + " for the checksum file");
                            }
                            string text = await hashResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                            expectedHash = Sha256Hex.ParseHashText(text);
                        }
                        if (expectedHash == null)
                        {
                            throw new UpdateVerificationException("the checksum file " + release.InstallerHash.Name + " on github.com holds no SHA-256");
                        }
                    }

                    using (HttpResponseMessage response = await client.GetAsync(release.Installer.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException("github.com answered " + (int)response.StatusCode + " " + response.ReasonPhrase + " for the setup file");
                        }
                        long total = response.Content.Headers.ContentLength ?? release.Installer.Size;
                        long done = 0;
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
                        {
                            var buffer = new byte[BufferSize];
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                                done += read;
                                if (progress != null)
                                {
                                    progress.Report(new DownloadProgress(done, total));
                                }
                            }
                        }
                    }

                    long actual = new FileInfo(part).Length;
                    if (release.Installer.Size > 0 && actual != release.Installer.Size)
                    {
                        throw new UpdateVerificationException("the downloaded file is incomplete (" + actual + " of " + release.Installer.Size + " bytes)");
                    }
                    if (expectedHash != null)
                    {
                        string actualHash = Sha256Hex.OfFile(part);
                        if (!Sha256Hex.Equal(actualHash, expectedHash))
                        {
                            Log.Error("Update: checksum mismatch for " + part + ": expected " + expectedHash + ", got " + actualHash + ".");
                            throw new UpdateVerificationException("the downloaded file is damaged (its checksum differs from the one on github.com)");
                        }
                        Log.Info("Update: " + actual + " bytes downloaded; the checksum " + actualHash + " matches.");
                    }
                    else
                    {
                        Log.Warn("Update: release " + release.Tag + " has no checksum file; only the size (" + actual + " bytes) was checked.");
                    }
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(part, target);
                return target;
            }
            catch
            {
                TryDelete(part);
                throw;
            }
        }

        /// <summary>Plain language for the Settings tab; the full exception is in the log.</summary>
        public static string DescribeError(Exception ex)
        {
            if (ex == null)
            {
                return "unknown error";
            }
            if (ex is UpdateVerificationException)
            {
                return ex.Message;
            }
            if (ex is FormatException)
            {
                return "github.com answered with something PrintVect did not understand (" + ex.Message + ")";
            }
            if (ex is OperationCanceledException)
            {
                return "github.com did not answer in time";
            }

            for (Exception inner = ex; inner != null; inner = inner.InnerException)
            {
                var web = inner as WebException;
                if (web != null)
                {
                    switch (web.Status)
                    {
                        case WebExceptionStatus.NameResolutionFailure:
                        case WebExceptionStatus.ProxyNameResolutionFailure:
                        case WebExceptionStatus.ConnectFailure:
                            return "this PC cannot reach github.com (is it connected to the internet?)";
                        case WebExceptionStatus.Timeout:
                            return "github.com did not answer in time";
                        case WebExceptionStatus.TrustFailure:
                        case WebExceptionStatus.SecureChannelFailure:
                            return "the secure connection to github.com failed (" + web.Message + ")";
                        default:
                            return "github.com could not be reached: " + web.Message;
                    }
                }
                if (inner is SocketException)
                {
                    return "this PC cannot reach github.com (is it connected to the internet?)";
                }
            }
            if (ex is HttpRequestException)
            {
                return "github.com could not be reached: " + ex.Message;
            }
            if (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "the file could not be written: " + ex.Message;
            }
            return ex.Message;
        }

        private static UpdateCheckResult Fail(UpdateCheckResult result, string error)
        {
            Log.Warn("Update check: " + error + ".");
            result.Outcome = UpdateCheckOutcome.Failed;
            result.Error = error;
            return result;
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            // Windows 7 / 8.1 do not offer TLS 1.2 to .NET programs unless asked; GitHub needs it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = true };
            try
            {
                IWebProxy proxy = WebRequest.GetSystemWebProxy();
                proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                handler.Proxy = proxy;
            }
            catch (Exception ex)
            {
                Log.Warn("Update: the Windows proxy settings could not be read: " + ex.Message);
            }

            var client = new HttpClient(handler, true) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        private static string SafeFileName(string name)
        {
            string fileName = Path.GetFileName(name ?? "");
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(bad, '_');
            }
            return fileName.Length == 0 ? "PrintVect-Setup.exe" : fileName;
        }

        private static void RemoveOtherFiles(string folder, string keep)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex)
            {
                Log.Warn("Update: could not list " + folder + ": " + ex.Message);
                return;
            }
            foreach (string file in files)
            {
                if (!string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                }
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Update: could not remove " + file + ": " + ex.Message);
            }
        }
    }
}
