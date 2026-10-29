using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Host
{
    /// <summary>spool\incoming\: where received files wait for the printer (brief 5.3).</summary>
    public sealed class IncomingSpool
    {
        public const string FolderName = "incoming";

        public IncomingSpool(AppPaths paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            Directory = Path.Combine(paths.SpoolDir, FolderName);
        }

        public string Directory { get; }

        public void Ensure()
        {
            System.IO.Directory.CreateDirectory(Directory);
        }

        /// <summary>The job id is a GUID and the format is "xps" or "oxps", so the name is always safe.</summary>
        public string PathFor(string jobId, string format)
        {
            return Path.Combine(Directory, jobId + "." + format);
        }

        public void DeleteQuietly(string path, string jobId)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Info(jobId, "Deleted " + path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Could not delete " + path + ": " + ex.Message);
            }
        }

        /// <summary>The size of every file in the folder together.</summary>
        public long TotalBytes()
        {
            return Files().Sum(f => f.Length);
        }

        /// <summary>
        /// Removes received files no job waits for any more (kept after a failed print, or left by an earlier run)
        /// that are older than <paramref name="age"/>. Returns how many were deleted.
        /// </summary>
        public int DeleteUnusedOlderThan(TimeSpan age, ICollection<string> inUse)
        {
            DateTime cutoff = DateTime.Now - age;
            int deleted = 0;
            foreach (FileInfo file in Unused(inUse).Where(f => f.LastWriteTime < cutoff))
            {
                if (TryDelete(file)) deleted++;
            }
            if (deleted > 0)
            {
                Log.Info("Deleted " + deleted + " received file(s) older than " + age.TotalHours + " hours from " + Directory);
            }
            return deleted;
        }

        /// <summary>Deletes unused files, oldest first, until <paramref name="bytes"/> are free. Returns the bytes freed.</summary>
        public long FreeUnused(long bytes, ICollection<string> inUse)
        {
            long freed = 0;
            foreach (FileInfo file in Unused(inUse).OrderBy(f => f.LastWriteTimeUtc))
            {
                if (freed >= bytes) break;
                long length = file.Length;
                if (TryDelete(file)) freed += length;
            }
            if (freed > 0)
            {
                Log.Info("Deleted " + freed + " bytes of older received files to make room in " + Directory);
            }
            return freed;
        }

        private IEnumerable<FileInfo> Files()
        {
            var folder = new DirectoryInfo(Directory);
            return folder.Exists ? folder.GetFiles() : new FileInfo[0];
        }

        private IEnumerable<FileInfo> Unused(ICollection<string> inUse)
        {
            return Files().Where(f => inUse == null || !inUse.Contains(f.FullName)).ToList();
        }

        private static bool TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not delete the received file " + file.FullName + ": " + ex.Message);
                return false;
            }
        }
    }
}
