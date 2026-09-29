using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PrintVect.Core.Logging
{
    /// <summary>
    /// PrintVect's own minimal logger (brief, section 12): one file per day per program
    /// ("PrintVect-2026-09-29.log", "PrintVect-Elevate-2026-09-29.log"), 14-day retention,
    /// no dependencies. Every job step is logged with its jobId so a failed job can be traced
    /// from the client's and the host's files. Logging never throws.
    /// </summary>
    public static class Log
    {
        public const string DefaultPrefix = "PrintVect";
        public const int DefaultRetentionDays = 14;

        private static readonly object Gate = new object();
        private static readonly Regex FileNamePattern = new Regex(
            @"^(?<prefix>.+)-(?<date>\d{4}-\d{2}-\d{2})\.log$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string _directory;
        private static string _prefix = DefaultPrefix;
        private static int _retentionDays = DefaultRetentionDays;
        private static DateTime _openDay;
        private static StreamWriter _writer;
        private static string _currentPath;

        /// <summary>Folder the log files live in, or null before Initialize.</summary>
        public static string Directory
        {
            get { lock (Gate) { return _directory; } }
        }

        public static string Prefix
        {
            get { lock (Gate) { return _prefix; } }
        }

        public static bool IsInitialized
        {
            get { lock (Gate) { return _writer != null; } }
        }

        /// <summary>Full path of the file being written, or null before Initialize.</summary>
        public static string CurrentFile
        {
            get { lock (Gate) { return _currentPath; } }
        }

        /// <summary>
        /// Files under %ProgramData% belong to the user who created them. When today's file was
        /// created by another user (or is locked), this program writes to a per-user file instead,
        /// e.g. "PrintVect-spm-2026-09-29.log", so PrintVect still starts.
        /// </summary>
        public static string FallbackPrefix(string prefix)
        {
            return prefix + "-" + SafeUserName();
        }

        /// <summary>Opens today's file in <paramref name="directory"/> and deletes files older than the retention.</summary>
        public static void Initialize(string directory, string prefix = DefaultPrefix, int retentionDays = DefaultRetentionDays)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A log folder is required.", nameof(directory));
            }
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("A file prefix is required.", nameof(prefix));
            }

            lock (Gate)
            {
                CloseWriter();
                _directory = Path.GetFullPath(directory);
                _prefix = prefix;
                _retentionDays = Math.Max(1, retentionDays);
                System.IO.Directory.CreateDirectory(_directory);
                OpenWriter(DateTime.Now);
                int deleted = CleanupOldFiles(_directory, _retentionDays, DateTime.Today);
                if (deleted > 0)
                {
                    Info(string.Format(CultureInfo.InvariantCulture, "Deleted {0} log file(s) older than {1} days.", deleted, _retentionDays));
                }
            }
        }

        /// <summary>Flushes and closes the file. Later calls go to the debugger trace only.</summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                CloseWriter();
                _directory = null;
            }
        }

        public static void Info(string message) { Write("INFO", null, message, null); }
        public static void Info(string jobId, string message) { Write("INFO", jobId, message, null); }

        public static void Warn(string message) { Write("WARN", null, message, null); }
        public static void Warn(string jobId, string message) { Write("WARN", jobId, message, null); }

        public static void Error(string message) { Write("ERROR", null, message, null); }
        public static void Error(string message, Exception ex) { Write("ERROR", null, message, ex); }
        public static void Error(string jobId, string message, Exception ex) { Write("ERROR", jobId, message, ex); }

        /// <summary>"PrintVect-2026-09-29.log"</summary>
        public static string FileNameFor(string prefix, DateTime day)
        {
            return prefix + "-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
        }

        /// <summary>Reads the prefix and the day back out of a file name produced by <see cref="FileNameFor"/>.</summary>
        public static bool TryParseFileName(string fileName, out string prefix, out DateTime day)
        {
            prefix = null;
            day = default(DateTime);
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            Match m = FileNamePattern.Match(fileName);
            if (!m.Success)
            {
                return false;
            }

            if (!DateTime.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day))
            {
                return false;
            }
            prefix = m.Groups["prefix"].Value;
            return true;
        }

        /// <summary>
        /// Deletes every *.log file in <paramref name="directory"/> whose date is
        /// <paramref name="retentionDays"/> or more days before <paramref name="today"/>.
        /// Returns how many were deleted. Files that cannot be deleted are left for next time.
        /// </summary>
        public static int CleanupOldFiles(string directory, int retentionDays, DateTime today)
        {
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            {
                return 0;
            }

            int deleted = 0;
            foreach (string file in System.IO.Directory.GetFiles(directory, "*.log"))
            {
                string prefix;
                DateTime day;
                if (!TryParseFileName(Path.GetFileName(file), out prefix, out day))
                {
                    continue;
                }
                if ((today.Date - day.Date).TotalDays < retentionDays)
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Warn("Could not delete old log file " + file + ": " + ex.Message);
                }
            }
            return deleted;
        }

        /// <summary>
        /// The last <paramref name="maxLines"/> lines written by this program, oldest first,
        /// reading back through previous days' files when today's is short.
        /// </summary>
        public static IList<string> TailLines(int maxLines)
        {
            string directory;
            string prefix;
            lock (Gate)
            {
                directory = _directory;
                prefix = _prefix;
            }

            var result = new List<string>();
            if (maxLines <= 0 || directory == null || !System.IO.Directory.Exists(directory))
            {
                return result;
            }

            var files = new List<KeyValuePair<DateTime, string>>();
            foreach (string file in System.IO.Directory.GetFiles(directory, prefix + "-*.log"))
            {
                string filePrefix;
                DateTime day;
                if (TryParseFileName(Path.GetFileName(file), out filePrefix, out day)
                    && (filePrefix == prefix || filePrefix == FallbackPrefix(prefix)))
                {
                    files.Add(new KeyValuePair<DateTime, string>(day, file));
                }
            }

            foreach (var entry in files.OrderByDescending(f => f.Key))
            {
                if (result.Count >= maxLines)
                {
                    break;
                }

                string[] lines;
                try
                {
                    lines = ReadAllLinesShared(entry.Value);
                }
                catch (Exception ex)
                {
                    result.Insert(0, "(could not read " + entry.Value + ": " + ex.Message + ")");
                    continue;
                }

                int wanted = maxLines - result.Count;
                int take = Math.Min(wanted, lines.Length);
                result.InsertRange(0, lines.Skip(lines.Length - take).Take(take));
            }
            return result;
        }

        private static string Stamp(string level, string jobId, string message)
        {
            var sb = new StringBuilder(160);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(' ').Append(level.PadRight(5));
            sb.Append(" [t").Append(Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture)).Append(']');
            if (!string.IsNullOrEmpty(jobId))
            {
                sb.Append(" [job ").Append(jobId).Append(']');
            }
            sb.Append(' ').Append(message ?? "");
            return sb.ToString();
        }

        private static void Write(string level, string jobId, string message, Exception ex)
        {
            DateTime now = DateTime.Now;
            string line = Stamp(level, jobId, message);
            if (ex != null)
            {
                line += Environment.NewLine + "    " + ex.ToString().Replace("\n", "\n    ");
            }

            lock (Gate)
            {
                if (_writer == null)
                {
                    System.Diagnostics.Trace.WriteLine(line);
                    return;
                }

                try
                {
                    if (now.Date != _openDay)
                    {
                        CloseWriter();
                        OpenWriter(now);
                        CleanupOldFiles(_directory, _retentionDays, now.Date);
                    }
                    _writer.WriteLine(line);
                }
                catch (Exception writeEx)
                {
                    // Logging must never take the program down; the trace listener is the last resort.
                    System.Diagnostics.Trace.WriteLine("PrintVect log write failed: " + writeEx.Message + " | " + line);
                }
            }
        }

        private static void OpenWriter(DateTime day)
        {
            string path = Path.Combine(_directory, FileNameFor(_prefix, day));
            try
            {
                _writer = CreateWriter(path);
                _currentPath = path;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                string fallback = Path.Combine(_directory, FileNameFor(FallbackPrefix(_prefix), day));
                _writer = CreateWriter(fallback);
                _currentPath = fallback;
                _writer.WriteLine(Stamp("WARN", null,
                    "Could not open " + path + " (" + ex.Message + "); writing to " + fallback + " instead."));
            }
            _openDay = day.Date;
        }

        private static StreamWriter CreateWriter(string path)
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };
        }

        private static string SafeUserName()
        {
            string name = Environment.UserName ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            name = name.Replace('-', '_').Trim();
            return name.Length == 0 ? "user" : name;
        }

        private static void CloseWriter()
        {
            if (_writer == null)
            {
                return;
            }
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("PrintVect log close failed: " + ex.Message);
            }
            _writer = null;
            _currentPath = null;
        }

        /// <summary>Reads a file that another process (or this one) may still be appending to.</summary>
        private static string[] ReadAllLinesShared(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }
            return lines.ToArray();
        }
    }
}
