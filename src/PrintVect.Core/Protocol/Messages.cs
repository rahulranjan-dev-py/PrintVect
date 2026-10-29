using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrintVect.Core.Protocol
{
    /// <summary>Values of the "type" field of a request header.</summary>
    public static class RequestTypes
    {
        public const string Job = "job";
        public const string Status = "status";

        /// <summary>Asks a host for its shared printers over TCP (used by pvct-send and by "Add by IP").</summary>
        public const string List = "list";

        /// <summary>
        /// "This PC uses your printers" (M6b-2): a client tells each host it prints to that it is there, with its Windows
        /// and PrintVect version, so the host can show the PCs that use it. Sent every few minutes; no body.
        /// </summary>
        public const string Hello = "hello";
    }

    /// <summary>Values of the "format" field: which XPS flavour the file is.</summary>
    public static class JobFormats
    {
        public const string Xps = "xps";
        public const string Oxps = "oxps";

        public static bool IsKnown(string format)
        {
            return format == Xps || format == Oxps;
        }

        /// <summary>"xps" for .xps, "oxps" for .oxps, null for anything else.</summary>
        public static string FromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (fileName.EndsWith(".xps", StringComparison.OrdinalIgnoreCase)) return Xps;
            if (fileName.EndsWith(".oxps", StringComparison.OrdinalIgnoreCase)) return Oxps;
            return null;
        }
    }

    /// <summary>Values of the "state" field of a job reply.</summary>
    public static class JobStates
    {
        public const string Queued = "queued";
        public const string Printing = "printing";
        public const string Printed = "printed";
        public const string Error = "error";

        public static bool IsFinal(string state)
        {
            return state == Printed || state == Error;
        }
    }

    /// <summary>The JSON header that starts every TCP request.</summary>
    public sealed class RequestHeader
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("v")]
        public int Version { get; set; } = ProtocolConstants.Version;

        [JsonProperty("printerId", NullValueHandling = NullValueHandling.Ignore)]
        public string PrinterId { get; set; }

        [JsonProperty("jobId", NullValueHandling = NullValueHandling.Ignore)]
        public string JobId { get; set; }

        [JsonProperty("fileName", NullValueHandling = NullValueHandling.Ignore)]
        public string FileName { get; set; }

        [JsonProperty("format", NullValueHandling = NullValueHandling.Ignore)]
        public string Format { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("client", NullValueHandling = NullValueHandling.Ignore)]
        public string Client { get; set; }

        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)]
        public string User { get; set; }

        [JsonProperty("doc", NullValueHandling = NullValueHandling.Ignore)]
        public string Doc { get; set; }

        /// <summary>
        /// Before 0.5.8: SHA-256 hex of the sharing PIN. No longer sent; a host that sees it tells the sender to update.
        /// </summary>
        [JsonProperty("pin", NullValueHandling = NullValueHandling.Ignore)]
        public string LegacyPinHash { get; set; }

        /// <summary>0.5.8: a random value that makes the PIN proof of this request one of a kind.</summary>
        [JsonProperty("nonce", NullValueHandling = NullValueHandling.Ignore)]
        public string Nonce { get; set; }

        /// <summary>0.5.8: when the PIN proof was made, in seconds since 1970 (UTC).</summary>
        [JsonProperty("time", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long Time { get; set; }

        /// <summary>0.5.8: the one-time proof that the sender knows the sharing PIN (PinProof); absent without a PIN.</summary>
        [JsonProperty("pinProof", NullValueHandling = NullValueHandling.Ignore)]
        public string Proof { get; set; }

        /// <summary>List only (0.5.8): a random value the host signs with its identity key (ListReply.HostSig).</summary>
        [JsonProperty("challenge", NullValueHandling = NullValueHandling.Ignore)]
        public string Challenge { get; set; }

        /// <summary>The PIN that JobClient proves just before sending. Never written to the network.</summary>
        [JsonIgnore]
        public string PinToSign { get; set; }

        /// <summary>Copies the host should print; 1 when absent. Read from the XPS print ticket on the client (M3).</summary>
        [Newtonsoft.Json.JsonProperty("copies", DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public int Copies { get; set; } = 1;

        /// <summary>Hello only: the sender's Windows edition, e.g. "Windows 10 Pro 22H2 (build 19045.4894)".</summary>
        [JsonProperty("os", NullValueHandling = NullValueHandling.Ignore)]
        public string Os { get; set; }

        /// <summary>Hello only: the sender's PrintVect version, e.g. "0.4.0".</summary>
        [JsonProperty("appVersion", NullValueHandling = NullValueHandling.Ignore)]
        public string AppVersion { get; set; }

        /// <summary>Hello only: the ids of this host's printers that the sender has added.</summary>
        [JsonProperty("printers", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Printers { get; set; }

        public static RequestHeader ForHello(string os, string appVersion, IEnumerable<string> printerIds, string pin = null)
        {
            return new RequestHeader
            {
                Type = RequestTypes.Hello,
                Client = Environment.MachineName,
                User = Environment.UserName,
                Os = os,
                AppVersion = appVersion,
                Printers = printerIds == null ? new List<string>() : new List<string>(printerIds),
                PinToSign = pin
            };
        }

        public static RequestHeader ForList(string challenge = null)
        {
            return new RequestHeader { Type = RequestTypes.List, Challenge = challenge };
        }

        public static RequestHeader ForStatus(string jobId)
        {
            return new RequestHeader { Type = RequestTypes.Status, JobId = jobId };
        }

        public static RequestHeader ForJob(string printerId, string fileName, string format, long size, string doc, string pin)
        {
            return new RequestHeader
            {
                Type = RequestTypes.Job,
                PrinterId = printerId,
                JobId = Guid.NewGuid().ToString("D"),
                FileName = fileName,
                Format = format,
                Size = size,
                Client = Environment.MachineName,
                User = Environment.UserName,
                Doc = doc ?? "",
                PinToSign = pin
            };
        }
    }

    /// <summary>The one JSON line a host answers to a job or status request.</summary>
    public sealed class JobReply
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("jobId", NullValueHandling = NullValueHandling.Ignore)]
        public string JobId { get; set; }

        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public string State { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        /// <summary>
        /// The host lost this job before it printed (PrintVect there was closed, or sharing was turned off), so
        /// sending the same job again makes sense. Added in 0.4.0; older hosts never send it.
        /// </summary>
        [JsonProperty("retry", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Retry { get; set; }

        public static JobReply Error(string jobId, string message)
        {
            return new JobReply { Ok = false, JobId = jobId, State = JobStates.Error, Message = message };
        }
    }

    /// <summary>One shared printer as a host describes it (discovery reply and list reply).</summary>
    public sealed class PrinterInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>The Windows printer name on the host.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>What clerks see, e.g. "Counter 1 Laser".</summary>
        [JsonProperty("friendly")]
        public string Friendly { get; set; }

        /// <summary>ready, busy, offline, paper out, paused, error or unknown.</summary>
        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// A host's description of itself: the discovery reply (M3) and the answer to a "list" request.
    /// A refused list request comes back with ok=false and a message, and no printers.
    /// </summary>
    public sealed class ListReply
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; } = true;

        [JsonProperty("app")]
        public string App { get; set; } = AppInfo.ProductName;

        [JsonProperty("v")]
        public int Version { get; set; } = ProtocolConstants.Version;

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("ip")]
        public string Ip { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("printers")]
        public List<PrinterInfo> Printers { get; set; } = new List<PrinterInfo>();

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        /// <summary>0.5.8: the host's public identity key (HostKeys); a client remembers its fingerprint with each printer.</summary>
        [JsonProperty("hostKey", NullValueHandling = NullValueHandling.Ignore)]
        public string HostKey { get; set; }

        /// <summary>0.5.8, list replies to a request with a challenge: the host's signature of challenge, name and address.</summary>
        [JsonProperty("hostSig", NullValueHandling = NullValueHandling.Ignore)]
        public string HostSig { get; set; }
    }
}
