# PrintVect wire protocol, version 1

PrintVect is the chosen product name for the tool described in
`docs/MudranSetu_Claude_Code_Guide.pdf` (section 6). This page is the
protocol from that brief with the PrintVect identifiers substituted.
Everything here is LAN-only; nothing ever leaves the office network.

## Identifiers

| Brief (MudranSetu) | PrintVect            | Where it is used                      |
|--------------------|----------------------|---------------------------------------|
| `MSETU-DISCOVER 1` | `PVECT-DISCOVER 1`   | UDP discovery request text            |
| `MSTU`             | `PVCT`               | 4-byte magic at the start of a TCP job |
| `"app":"MudranSetu"` | `"app":"PrintVect"` | `app` field of the discovery reply    |
| `mstu-send.exe`    | `pvct-send.exe`      | M1 command-line test sender           |

Ports are unchanged: **UDP 9150** for discovery, **TCP 9151** for jobs.
Both are configurable in Settings; **9100** (reserved for the Phase 2 raw
relay) and **631** are never allowed.

## Channels

| Channel           | Transport                                 | Format |
|-------------------|-------------------------------------------|--------|
| Discovery request | UDP broadcast to `255.255.255.255:9150`   | ASCII text `PVECT-DISCOVER 1`. Sent every 10 s while the *Use shared printers* tab is open, plus on demand. |
| Discovery reply   | UDP unicast back to the sender            | One JSON object, see below. |
| Job transfer      | TCP 9151, one job per connection          | `PVCT` (4 ASCII bytes), 4-byte little-endian header length, UTF-8 JSON header, then the raw file bytes. The host answers one JSON line, then closes. |
| Status query      | TCP 9151                                  | Same framing with header `{"type":"status","jobId":"guid"}` and no body. Same reply format. |
| Printer list      | TCP 9151                                  | Same framing with header `{"type":"list","v":1}` and no body. The host answers the discovery-reply JSON below plus `"ok":true`. Used by `pvct-send list` and by manual "Add by IP" (M3), which cannot rely on UDP broadcast. |

## Discovery reply

```json
{
  "app": "PrintVect",
  "v": 1,
  "host": "COUNTER1",
  "ip": "192.168.1.5",
  "port": 9151,
  "printers": [
    { "id": "…", "name": "HP LaserJet 1020", "friendly": "Counter 1 Laser", "status": "ready" }
  ]
}
```

## Job header

```json
{
  "type": "job",
  "v": 1,
  "printerId": "…",
  "jobId": "guid",
  "fileName": "job.xps",
  "format": "xps",
  "size": 123456,
  "client": "COUNTER3",
  "user": "spm",
  "doc": "optional document title",
  "pin": "sha256-hex of the shared PIN, or empty"
}
```

`copies` (optional, default 1, at most 99) asks the host to print the document that many times; the client
fills it from the XPS print ticket inside the file (`JobCopiesAllDocuments`), which Word and Excel set when the
user chooses copies. A host that receives no `copies` reads the ticket itself.

`format` is `xps` or `oxps` (the Windows 8+ v4 XPS writer produces OpenXPS). The host does not trust
the field blindly: it looks inside the package (the OpenXPS namespace `schemas.openxps.org/oxps/v1.0`
versus the XPS namespace `schemas.microsoft.com/xps/2005/06`) and prints by what it finds, logging a
mismatch. The client sets `doc` from the package's `docProps/core.xml` title when the XPS writer stored one.

## Job reply

```json
{ "ok": true, "jobId": "guid", "state": "queued", "message": "Sent to COUNTER1, printing" }
```

`state` is one of `queued`, `printing`, `printed`, `error`.

## Refusals

A refused request (wrong PIN, unknown printer, unsupported format, wrong protocol version,
file too large, not an XPS file) is answered with `{"ok":false,"state":"error","message":"..."}`
in plain language. The host reads and discards the rest of a refused job body first, so the
sender always receives the reason instead of a broken connection. A job whose body ends early
gets no reply; the host logs it and stays up.

Ids sent as `printerId` are matched first by id, then by friendly name, then by the Windows
printer name (case-insensitive), so a person can type a name into `pvct-send`.

## Rules

- Optional shared PIN, set in Settings on both sides. The header carries the
  SHA-256 of the PIN; the host rejects a mismatch with a clear message.
  An empty PIN means open to the LAN.
- Limits: max file 200 MB, connect timeout 5 s, transfer timeout 5 min,
  discovery timeout 2 s.
- Manual host entry (IP or PC name) is always available, because UDP
  broadcast does not cross subnets or VLANs.
- Every job step is logged with its `jobId` on both sides, so a failed job
  can be traced from the two log files.

## Milestone status

| Part                                   | Milestone | State                       |
|----------------------------------------|-----------|-----------------------------|
| Framing + JSON classes (`Core/Protocol`) | M1      | done                        |
| TCP job, status and list requests (host) | M1      | done                        |
| Client sender (`JobClient`, pvct-send)   | M1      | done                        |
| PIN check on the host                    | M1      | done (PIN set in config.json until the Settings UI arrives) |
| UDP discovery                            | M3      | done                        |
