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

        /// <summary>SHA-256 hex of the shared PIN, or empty/absent when no PIN is used.</summary>
        [JsonProperty("pin", NullValueHandling = NullValueHandling.Ignore)]
        public string Pin { get; set; }

        public static RequestHeader ForList()
        {
            return new RequestHeader { Type = RequestTypes.List };
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
                Pin = string.IsNullOrEmpty(pin) ? "" : PinHash.Compute(pin)
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
    }
}
