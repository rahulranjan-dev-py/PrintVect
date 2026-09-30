using System;
using System.IO;
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

        /// <summary>Removes leftovers from earlier runs. Returns how many files were deleted.</summary>
        public int SweepOlderThan(TimeSpan age)
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return 0;
            }

            int deleted = 0;
            DateTime cutoff = DateTime.Now - age;
            foreach (string file in System.IO.Directory.GetFiles(Directory))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not delete old spool file " + file + ": " + ex.Message);
                }
            }
            if (deleted > 0)
            {
                Log.Info("Deleted " + deleted + " received file(s) older than " + age.TotalHours + " hours from " + Directory);
            }
            return deleted;
        }
    }
}
