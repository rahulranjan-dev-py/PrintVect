# PrintVect wire protocol, version 1

PrintVect is the chosen product name for the tool described in
`docs/brief.md` (section 6). This page is the
protocol from that brief with the PrintVect identifiers substituted.
Everything here is LAN-only; nothing ever leaves the local network.

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
| Discovery request | UDP broadcast to `255.255.255.255:9150`   | ASCII text `PVECT-DISCOVER 1`. Sent every 10 s while the *Printers on other PCs* page (*Office printers* until 0.5.4) is open, on **Search again**, and once (listening 3 s) when a job cannot reach its host: a host on a phone hotspot gets a new address each time it connects, and the job is then sent to the address it answers from (0.5.0). |
| Discovery reply   | UDP unicast back to the sender            | One JSON object, see below. |
| Job transfer      | TCP 9151, one job per connection          | `PVCT` (4 ASCII bytes), 4-byte little-endian header length, UTF-8 JSON header, then the raw file bytes. The host answers one JSON line, then closes. |
| Status query      | TCP 9151                                  | Same framing with header `{"type":"status","jobId":"guid"}` and no body. Same reply format. |
| Printer list      | TCP 9151                                  | Same framing with header `{"type":"list","v":1}` and no body. The host answers the discovery-reply JSON below plus `"ok":true`. Used by `pvct-send list` and by manual "Add by IP" (M3), which cannot rely on UDP broadcast. |
| Hello (presence)  | TCP 9151                                  | Same framing with header `{"type":"hello","v":1,"client":"COUNTER3","user":"clerk","os":"Windows 10 Pro 22H2 (build 19045.4894)","appVersion":"0.4.0","printers":["…"]}` and no body (0.4.0, M6b-2). A client sends it 15 s after start, after adding a printer, and every 5 minutes to each host whose printers it has added. The host answers `{"ok":true,"message":"…"}` and lists the PC on its Share page; a job also marks its PC as seen. A host before 0.4.0 answers ok=false ("unknown request type"), which the client ignores. |

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
  "user": "user1",
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

`retry` (0.4.0, optional, default false) on an `ok:false` reply means the job did not print but sending the same
job again makes sense: the host lost it (PrintVect there was closed, or sharing went off while its printer was
paused; also a status request for a job the host does not know), or it was refused for the sharing PIN. The client
then keeps the file for **Retry waiting** instead of filing it as failed. A host that receives a job id it
already printed, or still has queued, answers with that job's state and does not print it again, so Retry never
prints a page twice.

A job for a paused printer is accepted at once with state `queued` and the message "Waiting on HOST: … is paused
there"; the list and discovery replies give that printer the status `paused`.

## Refusals

A refused request (wrong PIN, unknown printer, unsupported format, wrong protocol version,
file too large, not an XPS file) is answered with `{"ok":false,"state":"error","message":"..."}`
in plain language. The host reads and discards the rest of a refused job body first, so the
sender always receives the reason instead of a broken connection. A job whose body ends early
gets no reply; the host logs it and stays up.

Ids sent as `printerId` are matched first by id, then by friendly name, then by the Windows
printer name (case-insensitive), so a person can type a name into `pvct-send`.

## Rules

- Optional sharing PIN (the office PIN until 0.5.4), set in Settings on both sides (Set PIN…, 0.4.0). The header carries the
  SHA-256 of the PIN; the host rejects a mismatch with a clear message and `retry:true`.
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
| PIN check on the host                    | M1      | done; PIN screen in Settings since M6b-2 |
| UDP discovery                            | M3      | done                        |
| Hello (presence), `retry`, paused status, resend not printed twice | M6b-2 | done |
