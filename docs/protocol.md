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
| Hello (presence)  | TCP 9151                                  | Same framing with header `{"type":"hello","v":1,"client":"COUNTER3","user":"clerk","os":"Windows 10 Pro 22H2 (build 19045.4894)","appVersion":"0.4.0","printers":["…"]}` and no body (0.4.0, M6b-2). A client sends it 15 s after start, after adding a printer, and every 5 minutes to each host whose printers it has added. The host answers `{"ok":true,"message":"…"}` and lists the PC on its Share page; a job also marks its PC as seen. A host before 0.4.0 answers ok=false ("unknown request type"), which the client ignores. With a sharing PIN (0.5.8) the hello carries `nonce`, `time` and `pinProof` like a job; a host with a PIN lists only PCs that prove it, and a client whose hello is refused sends that host no more hellos until its PIN changes or it restarts. |

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
  ],
  "hostKey": "base64 RSA public key (0.5.8)"
}
```

### Host identity (0.5.8)

A PC name and printer ids travel openly, so any PC could claim them. Each host therefore makes an RSA-2048 identity
key on first start (kept in its `config.json`, protected with Windows DPAPI for that PC) and sends the public half as
`hostKey` (an RSA CSP blob in base64) in discovery and list replies. A list request may carry `"challenge"` (32 hex
digits); the reply then has `"hostSig"`: base64 RSA-SHA256 (PKCS#1 v1.5) signature of the UTF-8 text
`PrintVect host 1`, the challenge, the host name in capitals and the address the request reached (as the host's
socket sees it), joined by line feeds.

A client remembers the key's fingerprint (SHA-256 of the blob, hex) with each printer: when the printer is added,
proven with a challenge at the address it is added with, or, for printers added before 0.5.8, after the host's next
answered hello, proven the same way at the printer's own address. Before a printer follows its host to a new
address (discovery, or the search after a job could not reach it), the PC there must sign a fresh challenge for that
address with the remembered key; one that claims the host's name or printers without it gets no jobs, and the job
says why. A printer with no key yet remembers the first one proven to it; a host before 0.5.8 (no key) is followed
as before. The signed address stops a PC from passing on the real host's answer from elsewhere.

Since 0.5.9 the client also checks the PC at the printer's saved address before it sends a job there: the same list
request with a challenge, trusted for 5 minutes once passed. When it fails, nothing is sent; the client treats the
host as not reachable at that address (it searches for it, then keeps the job for Retry with the reason).

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
  "nonce": "32 hex digits, new for every request (only with a sharing PIN)",
  "time": 1793260800,
  "pinProof": "64 hex digits (only with a sharing PIN)"
}
```

The sharing PIN never travels (0.5.8). With a PIN set, the sender makes a one-time proof just before it sends:
`pinProof` = lowercase hex of PBKDF2-HMAC-SHA256 (20,000 rounds, 32 bytes) with the PIN in UTF-8 as the password and
as salt the UTF-8 text `PrintVect PIN 1`, type, nonce, time, printerId, jobId and size, joined by line feeds (an
absent field is empty). `time` is in seconds since 1970 (UTC); the host refuses a proof more than 24 hours away from
its own clock, and one made for another job, printer or size does not match. A host with a PIN refuses a request
without a proof with `retry:true`; a request with only the `pin` field of earlier versions (SHA-256 of the PIN) is
refused with a message to update the sender. After 5 wrong proofs from one address the host refuses that address for
15 minutes without checking, twice as long after each further 5 (at most 4 hours); a right PIN or a day without a
wrong one starts afresh. A hello proves the PIN the same way (with `printerId`, `jobId` and `size` empty and 0).
Since 0.5.9 the host remembers every `nonce` it accepted until its proof expires (`time` plus 24 hours) and refuses a
request that brings one again (a copy of a recorded request); that also counts as a wrong try. Every real send,
Retry included, makes a new nonce.

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

- Optional sharing PIN (the office PIN until 0.5.4), set in Settings on both sides (Set PIN…, 0.4.0). Each job and
  hello carries a one-time proof of it (0.5.8; before: its SHA-256); the host rejects a mismatch with a clear message
  and `retry:true`, and makes an address that sent 5 wrong PINs wait. An empty PIN means open to the LAN.
- Limits: max file 200 MB, connect timeout 5 s, transfer timeout 5 min,
  discovery timeout 2 s.
- Host limits (0.5.8): the request header must arrive within 15 s of connecting; at most 32 connections are served at
  once, 8 from one address, and 120 new ones a minute from one address (more are closed at once, logged once a minute).
  Received documents may take at most 1 GB in `spool\incoming\`, and a job is refused when it would leave less than
  256 MB free on that disk; older files no job waits for are deleted first to make room, and a refused job gets
  `retry:true`. A file that is not XPS is deleted at once; files kept after a failed print are deleted after 24 hours
  (checked every hour).
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
| PIN check on the host                    | M1      | done; PIN screen in Settings since M6b-2; one-time proof and wrong-PIN wait since 0.5.8 |
| UDP discovery                            | M3      | done                        |
| Hello (presence), `retry`, paused status, resend not printed twice | M6b-2 | done |
