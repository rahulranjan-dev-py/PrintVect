using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Send
{
    /// <summary>
    /// pvct-send: the milestone M1 test tool. Exit codes: 0 sent and printed (or still printing),
    /// 1 the host refused or the job failed, 2 wrong arguments, 3 the host could not be reached.
    /// </summary>
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitRefused = 1;
        private const int ExitUsage = 2;
        private const int ExitUnreachable = 3;
        private const string LogPrefix = "PrintVect-Send";
        private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan StatusPollTotal = TimeSpan.FromSeconds(60);

        private static int Main(string[] args)
        {
            InitializeLog();
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error: " + ex.Message);
                Log.Error("pvct-send failed.", ex);
                return ExitUnreachable;
            }
            finally
            {
                Log.Shutdown();
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            string error;
            Options options = Options.Parse(args, out error);
            if (error != null)
            {
                if (error.Length > 0)
                {
                    Console.Error.WriteLine(error);
                    Console.Error.WriteLine();
                }
                PrintUsage();
                return ExitUsage;
            }

            Log.Info("pvct-send " + AppInfo.Version + ": " + string.Join(" ", args));
            var client = new JobClient(options.Host, options.Port);
            try
            {
                switch (options.Command)
                {
                    case "list":
                        return await ListAsync(client).ConfigureAwait(false);
                    case "status":
                        return await StatusAsync(client, options.JobId).ConfigureAwait(false);
                    default:
                        return await SendAsync(client, options).ConfigureAwait(false);
                }
            }
            catch (HostUnreachableException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Log.Warn(ex.Message);
                return ExitUnreachable;
            }
            catch (TimeoutException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Log.Warn(ex.Message);
                return ExitUnreachable;
            }
            catch (ProtocolException ex)
            {
                Console.Error.WriteLine("The host answered something pvct-send does not understand: " + ex.Message);
                Log.Warn(ex.Message);
                return ExitUnreachable;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Log.Warn(ex.Message);
                return ExitUsage;
            }
        }

        private static async Task<int> ListAsync(JobClient client)
        {
            ListReply reply = await client.ListPrintersAsync(CancellationToken.None).ConfigureAwait(false);
            if (!reply.Ok)
            {
                Console.Error.WriteLine("The host refused: " + reply.Message);
                return ExitRefused;
            }

            Console.WriteLine("Host " + reply.Host + " (" + reply.Ip + ":" + reply.Port + ") shares " + reply.Printers.Count + " printer(s):");
            foreach (PrinterInfo printer in reply.Printers)
            {
                Console.WriteLine("  - \"" + printer.Friendly + "\"   Windows name: " + printer.Name + "   status: " + printer.Status + "   id: " + printer.Id);
            }
            if (reply.Printers.Count == 0)
            {
                Console.WriteLine("  (none: tick a printer under \"Share my printers\" on " + reply.Host + ")");
            }
            return ExitOk;
        }

        private static async Task<int> StatusAsync(JobClient client, string jobId)
        {
            JobReply reply = await client.QueryStatusAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine("Job " + reply.JobId + ": " + reply.State + " - " + reply.Message);
            return reply.Ok ? ExitOk : ExitRefused;
        }

        private static async Task<int> SendAsync(JobClient client, Options options)
        {
            string format = JobFormats.FromFileName(options.File);
            if (format == null)
            {
                Console.Error.WriteLine("The file must end with .xps or .oxps: " + options.File);
                return ExitUsage;
            }
            if (!File.Exists(options.File))
            {
                Console.Error.WriteLine("The file does not exist: " + options.File);
                return ExitUsage;
            }

            string printer = options.Printer;
            if (string.IsNullOrEmpty(printer))
            {
                ListReply list = await client.ListPrintersAsync(CancellationToken.None).ConfigureAwait(false);
                if (!list.Ok)
                {
                    Console.Error.WriteLine("The host refused: " + list.Message);
                    return ExitRefused;
                }
                if (list.Printers.Count != 1)
                {
                    Console.Error.WriteLine(list.Host + " shares " + list.Printers.Count
                                            + " printer(s); give the printer name as the third argument. Shared printers:");
                    foreach (PrinterInfo p in list.Printers)
                    {
                        Console.Error.WriteLine("  - \"" + p.Friendly + "\"");
                    }
                    return ExitUsage;
                }
                printer = list.Printers[0].Friendly;
            }

            string doc = string.IsNullOrEmpty(options.Doc) ? Path.GetFileNameWithoutExtension(options.File) : options.Doc;
            RequestHeader header = RequestHeader.ForJob(printer, Path.GetFileName(options.File), format, 0, doc, options.Pin);
            long size = new FileInfo(options.File).Length;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Sending \"{0}\" ({1:N0} bytes) to {2}:{3} for printer \"{4}\" as job {5} ...",
                Path.GetFileName(options.File), size, client.Host, client.Port, printer, header.JobId));

            var progress = new Progress<long>(sent =>
            {
                if (size >= 1024 * 1024)
                {
                    Console.Write("\r  sent " + (sent * 100 / size) + "%   ");
                }
            });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            JobReply reply = await client.SendJobAsync(header, options.File, progress, CancellationToken.None).ConfigureAwait(false);
            if (size >= 1024 * 1024)
            {
                Console.WriteLine();
            }
            Console.WriteLine("Host answered after " + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s: "
                              + reply.State + " - " + reply.Message);

            if (!reply.Ok)
            {
                return ExitRefused;
            }
            if (JobStates.IsFinal(reply.State) || options.NoWait)
            {
                return ExitOk;
            }

            Console.WriteLine("Asking " + client.Host + " every " + StatusPollInterval.TotalSeconds + " s for up to " + StatusPollTotal.TotalSeconds + " s ...");
            DateTime deadline = DateTime.UtcNow + StatusPollTotal;
            string lastState = reply.State;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(StatusPollInterval).ConfigureAwait(false);
                JobReply status = await client.QueryStatusAsync(header.JobId, CancellationToken.None).ConfigureAwait(false);
                if (status.State != lastState || status.Message != reply.Message)
                {
                    Console.WriteLine("  " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + status.State + " - " + status.Message);
                    lastState = status.State;
                    reply = status;
                }
                if (JobStates.IsFinal(status.State))
                {
                    return status.Ok ? ExitOk : ExitRefused;
                }
            }
            Console.WriteLine("Still " + lastState + " after " + StatusPollTotal.TotalSeconds + " s. Check the printer on " + client.Host
                              + ", or run: pvct-send status " + client.Host + " " + header.JobId);
            return ExitOk;
        }

        private static void InitializeLog()
        {
            try
            {
                AppPaths paths = AppPaths.Default();
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, LogPrefix);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("(no log file: " + ex.Message + ")");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("pvct-send " + AppInfo.Version + " - sends an XPS file to a PrintVect host for printing (test tool)");
            Console.WriteLine();
            Console.WriteLine("  pvct-send list <host> [/port 9151]");
            Console.WriteLine("        Shows the printers the host PC shares.");
            Console.WriteLine();
            Console.WriteLine("  pvct-send <file.xps | file.oxps> <host> [printer] [/pin <pin>] [/doc <title>] [/port 9151] [/nowait]");
            Console.WriteLine("        Prints the file on the host's shared printer (its friendly name, Windows name or id).");
            Console.WriteLine("        When the host shares exactly one printer, the name may be left out.");
            Console.WriteLine();
            Console.WriteLine("  pvct-send status <host> <jobId> [/port 9151]");
            Console.WriteLine("        Asks the host what happened to a job.");
            Console.WriteLine();
            Console.WriteLine("<host> is the other PC's name or IP address. Exit codes: 0 printed or still printing,");
            Console.WriteLine("1 refused or failed, 2 wrong arguments, 3 host not reachable.");
            Console.WriteLine("Log: " + Path.Combine(AppPaths.Default().LogsDir, LogPrefix + "-<date>.log"));
        }

        private sealed class Options
        {
            public string Command;
            public string Host;
            public int Port = AppConfig.DefaultJobPort;
            public string File;
            public string Printer;
            public string JobId;
            public string Pin = "";
            public string Doc;
            public bool NoWait;

            public static Options Parse(string[] args, out string error)
            {
                error = null;
                var options = new Options();
                var positional = new List<string>();

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.StartsWith("/", StringComparison.Ordinal) || arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        string key = arg.TrimStart('/', '-').ToLowerInvariant();
                        switch (key)
                        {
                            case "pin":
                            case "doc":
                            case "port":
                                if (i + 1 >= args.Length)
                                {
                                    error = "/" + key + " needs a value.";
                                    return options;
                                }
                                string value = args[++i];
                                if (key == "pin") options.Pin = value;
                                else if (key == "doc") options.Doc = value;
                                else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out options.Port)
                                         || !PortRules.IsAllowed(options.Port))
                                {
                                    error = "/port must be a number between " + PortRules.MinPort + " and " + PortRules.MaxPort
                                            + " (never " + PortRules.RawRelayPort + " or " + PortRules.IppPort + ").";
                                    return options;
                                }
                                break;
                            case "nowait":
                                options.NoWait = true;
                                break;
                            case "help":
                            case "?":
                            case "h":
                                error = "";
                                return options;
                            default:
                                error = "Unknown option " + arg + ".";
                                return options;
                        }
                    }
                    else
                    {
                        positional.Add(arg);
                    }
                }

                if (positional.Count == 0)
                {
                    error = "";
                    return options;
                }

                string first = positional[0];
                if (first.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    options.Command = "list";
                    if (positional.Count < 2) error = "list needs the host PC's name or IP address.";
                    else options.Host = positional[1];
                }
                else if (first.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    options.Command = "status";
                    if (positional.Count < 3) error = "status needs the host and the job id.";
                    else
                    {
                        options.Host = positional[1];
                        options.JobId = positional[2];
                    }
                }
                else
                {
                    options.Command = "send";
                    options.File = first;
                    if (positional.Count < 2) error = "Give the host PC's name or IP address after the file.";
                    else
                    {
                        options.Host = positional[1];
                        if (positional.Count > 2) options.Printer = positional[2];
                    }
                }
                return options;
            }
        }
    }
}
