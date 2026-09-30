using System;
using System.Diagnostics;
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
    /// (brief 5.3). Seen on the owner's Windows 11 PC: this call only returns once the printer port
    /// has taken the whole job (Microsoft Print to PDF: after the Save dialog is answered), and the
    /// job has usually already left the Windows queue by then. So: state "printing" is reported
    /// before the call, a job that is gone afterwards counts as printed, and a job still in the
    /// queue is watched for up to 60 s. The XPS path of System.Printing needs a single-threaded
    /// apartment, so the call always runs on an STA thread.
    /// </summary>
    public sealed class SystemPrintingEngine : IPrintEngine
    {
        public static readonly TimeSpan StatusWait = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ApartmentRunner.Run(ApartmentState.STA, "PrintVect System.Printing: " + request.JobId,
                () => PrintOnStaThread(request, onProgress));
        }

        private static PrintOutcome PrintOnStaThread(PrintRequest request, Action<string, string> onProgress)
        {
            string jobId = request.JobId;
            string host = Environment.MachineName;
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
                onProgress(JobStates.Printing, "Windows is sending the job to " + request.FriendlyName + " on " + host
                                               + ". If nothing comes out, check that printer.");

                PrintSystemJobInfo job;
                bool fastCopy = openXps;
                var watch = Stopwatch.StartNew();
                try
                {
                    Log.Info(jobId, "Submitting to \"" + queue.FullName + "\" as \"" + jobName + "\" (fastCopy=" + fastCopy
                                    + "). AddJob returns when the printer port has taken the whole job.");
                    job = queue.AddJob(jobName, request.FilePath, fastCopy);
                }
                catch (Exception ex) when (!fastCopy)
                {
                    Log.Warn(jobId, "AddJob with conversion failed after " + watch.ElapsedMilliseconds + " ms (" + ex.GetType().Name
                                    + ": " + ex.Message + "); retrying as a raw XPS copy.");
                    job = queue.AddJob(jobName, request.FilePath, true);
                    fastCopy = true;
                }

                Log.Info(jobId, "AddJob returned after " + watch.ElapsedMilliseconds + " ms: Windows job " + job.JobIdentifier
                                + " on \"" + queue.FullName + "\" (fastCopy=" + fastCopy + ").");
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
            string host = Environment.MachineName;
            string printedMessage = "Printed on " + request.FriendlyName + " (" + host + ").";
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
                    // Windows removes finished jobs from the queue; Refresh then fails. AddJob returned
                    // normally, so the port took the whole job: that is a print, not a loss.
                    Log.Info(jobId, "The job has left the Windows print queue (" + ex.GetType().Name + ": " + ex.Message + "); counting it as printed.");
                    return PrintOutcome.Printed(lastProblem == null
                        ? printedMessage
                        : "Printed on " + request.FriendlyName + " after the printer reported " + lastProblem + ".");
                }

                string status = job.JobStatus.ToString();
                if (status != lastStatus)
                {
                    Log.Info(jobId, "Windows print queue status: " + status);
                    lastStatus = status;
                }

                if (job.IsCompleted || job.IsPrinted)
                {
                    return PrintOutcome.Printed(printedMessage);
                }

                string problem = DescribeProblem(job);
                if (problem != null && problem != lastProblem)
                {
                    lastProblem = problem;
                    Log.Warn(jobId, "Printer problem reported by Windows: " + problem);
                }

                if (job.IsDeleted || job.IsDeleting)
                {
                    // Finished jobs pass through Deleting/Deleted without ever showing Printed (seen with
                    // Microsoft Print to PDF). Only a job that had a problem is reported as lost.
                    if (lastProblem == null)
                    {
                        Log.Info(jobId, "Windows is removing the finished job from the queue; counting it as printed.");
                        return PrintOutcome.Printed(printedMessage);
                    }
                    return PrintOutcome.Error("The job was removed from the Windows print queue on " + host
                                              + " after the printer reported " + lastProblem + ".");
                }

                Thread.Sleep(PollInterval);
            }

            if (lastProblem != null)
            {
                return PrintOutcome.Error("Not printed after " + StatusWait.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                                          + " seconds: the printer reports " + lastProblem
                                          + ". Fix the printer on " + host + "; the job stays in its Windows print queue.");
            }
            return PrintOutcome.StillPrinting("Still in the Windows print queue on " + host + " after "
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
