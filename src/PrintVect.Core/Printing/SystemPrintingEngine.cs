using System;
using System.Globalization;
using System.Printing;
using System.Threading;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// Prints a received XPS file with the host's own driver:
    /// PrintQueue.AddJob(name, path, fastCopy:false) lets Windows convert XPS for non-XPS drivers
    /// (brief 5.3). Then the Windows job is watched for up to 60 s to report printed or error.
    /// Runs on the print worker's STA thread only.
    /// </summary>
    public sealed class SystemPrintingEngine : IPrintEngine
    {
        public static readonly TimeSpan StatusWait = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string jobId = request.JobId;
            bool openXps = request.Format == JobFormats.Oxps;

            if (openXps && !WindowsInfo.IsWindows8OrLater)
            {
                Log.Warn(jobId, "OpenXPS (.oxps) job received on a Windows 7 host; Windows 7 cannot print it (brief 11.2).");
                return PrintOutcome.Error("This job is an OpenXPS (.oxps) file, which Windows 7 cannot print. "
                                          + "Share this printer from a Windows 10 or 11 PC, or print from a Windows 7 client.");
            }

            string jobName = BuildJobName(request);
            using (var server = new LocalPrintServer())
            using (PrintQueue queue = server.GetPrintQueue(request.PrinterName))
            {
                PrintSystemJobInfo job;
                bool fastCopy = openXps;
                try
                {
                    Log.Info(jobId, "Submitting to \"" + queue.FullName + "\" as \"" + jobName + "\" (fastCopy=" + fastCopy + ").");
                    job = queue.AddJob(jobName, request.FilePath, fastCopy);
                }
                catch (Exception ex) when (!fastCopy)
                {
                    Log.Warn(jobId, "AddJob with conversion failed (" + ex.GetType().Name + ": " + ex.Message + "); retrying as a raw XPS copy.");
                    job = queue.AddJob(jobName, request.FilePath, true);
                    fastCopy = true;
                }

                Log.Info(jobId, "Spooler accepted the job: Windows job " + job.JobIdentifier + " on \"" + queue.FullName + "\" (fastCopy=" + fastCopy + ").");
                onProgress(JobStates.Printing, "Printing on " + request.FriendlyName + " (" + Environment.MachineName + ").");
                return WaitForJob(job, request);
            }
        }

        private static string BuildJobName(PrintRequest request)
        {
            string doc = string.IsNullOrWhiteSpace(request.DocumentName) ? "document" : request.DocumentName.Trim();
            string from = string.IsNullOrWhiteSpace(request.ClientName) ? "" : " from " + request.ClientName.Trim();
            string name = "PrintVect: " + doc + from;
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        private static PrintOutcome WaitForJob(PrintSystemJobInfo job, PrintRequest request)
        {
            string jobId = request.JobId;
            DateTime deadline = DateTime.UtcNow + StatusWait;
            string lastStatus = null;
            string lastProblem = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    job.Refresh();
                }
                catch (Exception ex)
                {
                    // Windows removes finished jobs from the queue; Refresh then fails. No error seen means it printed.
                    Log.Info(jobId, "The job left the Windows print queue (" + ex.GetType().Name + ": " + ex.Message + ").");
                    return lastProblem == null
                        ? PrintOutcome.Printed("Printed on " + request.FriendlyName + " (" + Environment.MachineName + ").")
                        : PrintOutcome.Printed("Printed on " + request.FriendlyName + " after the printer reported: " + lastProblem + ".");
                }

                string status = job.JobStatus.ToString();
                if (status != lastStatus)
                {
                    Log.Info(jobId, "Windows print queue status: " + status);
                    lastStatus = status;
                }

                if (job.IsCompleted || job.IsPrinted)
                {
                    return PrintOutcome.Printed("Printed on " + request.FriendlyName + " (" + Environment.MachineName + ").");
                }
                if (job.IsDeleted)
                {
                    return PrintOutcome.Error("The job was removed from the Windows print queue before it printed"
                                              + (lastProblem == null ? "." : " (the printer reported: " + lastProblem + ")."));
                }

                string problem = DescribeProblem(job);
                if (problem != null && problem != lastProblem)
                {
                    lastProblem = problem;
                    Log.Warn(jobId, "Printer problem reported by Windows: " + problem);
                }

                Thread.Sleep(PollInterval);
            }

            if (lastProblem != null)
            {
                return PrintOutcome.Error("Not printed after " + StatusWait.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                                          + " seconds: the printer reports " + lastProblem
                                          + ". Fix the printer on " + Environment.MachineName + "; the job stays in its Windows print queue.");
            }
            return PrintOutcome.StillPrinting("Still in the Windows print queue on " + Environment.MachineName + " after "
                                              + StatusWait.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                                              + " seconds. It prints when the printer is ready.");
        }

        private static string DescribeProblem(PrintSystemJobInfo job)
        {
            if (job.IsPaperOut) return "paper out";
            if (job.IsOffline) return "printer offline";
            if (job.IsUserInterventionRequired) return "attention needed at the printer";
            if (job.IsInError) return "a printer error";
            if (job.IsBlocked) return "a blocked print queue";
            if (job.IsPaused) return "a paused job";
            return null;
        }
    }
}
