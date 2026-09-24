# VTM — Project State & Decision Log

**Single source of truth for where this project is and how it got here.**
Last snapshot update: **2026-09-24**

---

## How to use this file

This file has two halves that are maintained differently. Mixing them destroys the audit trail, so
keep the line between them strict.

| Part | Sections | Rule |
|---|---|---|
| **Current state** | 1 – 5 | **Rewritten** whenever reality changes. Always describes *now*. |
| **History** | 6 – 7 | **Append-only.** Never edit or delete an entry. Add a new one that supersedes it. |

Every entry in the log is dated. If a decision is reversed, do **not** delete it — add a new entry
that says what changed and why. The old entry stays so the reasoning is still readable later.

**When to update:** any time you finish a piece of work, make an architectural choice, hit a
blocker, or change something in sections 1–5. One line is enough. Cheap entries get written; long
ones don't.

---

## If you are an AI agent picking this project up

Read this whole file first, then the doc for whichever library you are touching. Specifically:

- **Sections 3 and 4** tell you what is built and what is deliberately *not* built.
- **Section 6** tells you which approaches were already considered and rejected, and why. Do not
  re-propose them without new information — the reasoning is recorded, argue against it explicitly
  if you disagree.
- **Section 5** is the list of known issues. If you are about to "discover" one of these, it is
  already known and tracked.
- Do not re-derive facts about the LiveKit libraries. They are documented in `docs/01`, `02`, `03`
  against the source checked out in this workspace.

This file, not any assistant's memory, is the source of truth. It must stand alone.

---

# CURRENT STATE

## 1. What this project is

A **VTM (Video Teller Machine)** system for a bank.

A customer walks up to a kiosk and starts a session. A "ring" goes out to a pool of available
tellers. One teller accepts, and a live video call begins. During the call:

- The kiosk publishes the **customer's camera, microphone, and its own screen** to the teller.
- The teller can see everything the customer sees, and **drive the kiosk application** through a
  defined command set.
- At the end, the session is submitted as a transaction and recorded.

The media transport is **LiveKit**, self-hosted. The control plane is a **.NET API** we are
building.

## 2. Architecture

```
        CONTROL PLANE (HTTP)                    MEDIA PLANE (WebRTC / WSS)
        ────────────────────                    ──────────────────────────

   ┌──────────────────────┐                            ┌──────────────┐
   │  Kiosk               │──── POST /api/... ────────▶│              │
   │  WPF + WebView2      │◀─── token ─────────────────│   LiveKit    │
   │  (livekit-client JS) │──── camera/mic/screen ────▶│   Server     │
   └──────────────────────┘                            │              │
                                                       │ :7880 http   │
   ┌──────────────────────┐                            │ :7881 tcp    │
   │  Teller              │──── POST /api/... ────────▶│ :50000-60000 │
   │  Angular             │◀─── token ─────────────────│    udp       │
   │  (livekit-client)    │──── camera/mic ───────────▶│              │
   └──────────────────────┘                            │              │
                                                       └──────▲───────┘
   ┌──────────────────────────────────┐                       │
   │  LivekitServerAPI                │─── Twirp/protobuf ────┘
   │  .NET 10 + FastEndpoints         │    tokens, rooms, participants,
   │  Livekit.Server.Sdk.Dotnet       │    PerformRpc, SendData, webhooks
   └──────────────────────────────────┘
```

**Repository layout**

| Path | What |
|---|---|
| `LivekitServerAPI/` | The control API we are building (.NET 10) |
| `vtm-kiosk/` | The customer-facing kiosk page (Angular 21) |
| `vtm-teller/` | The teller console (Angular 21 + MUK UI Kit) |
| `muk-demo/` | MUK UI Kit docs site — reference for the kit's components |
| `LivekitServer/` | Self-hosted `livekit-server.exe` + `livekit.yaml` + NSSM |
| `livekit-server-sdk-dotnet-main/` | Source checkout of the .NET SDKs (reference only) |
| `client-sdk-js-main/` | Source checkout of `livekit-client` (reference only) |
| `docs/` | This file and the library references |
| `setup-zip/` | Original downloads |

**Decisions in force** (see section 6 for the reasoning behind each)

| Area | Decision | Entry |
|---|---|---|
| Kiosk client | WPF shell hosting **WebView2 + `livekit-client`** | [D-007](#d-007) |
| Teller client | **Angular + `livekit-client`** | [D-007](#d-007) |
| Control API | **.NET 10 + FastEndpoints**, `Livekit.Server.Sdk.Dotnet` | [D-004](#d-004) |
| Native AOT | **Off.** ReadyToRun + self-contained instead | [D-003](#d-003) |
| Teller→kiosk control | **Application-level commands**, not desktop remote control | [D-009](#d-009) |
| Audit granularity | Submit-time + side-effect commands only | [D-010](#d-010) |
| `Livekit.Rtc.Dotnet` | **Parked** for a later server-side-participant feature | [D-008](#d-008) |
| Secrets | Stay in `appsettings.json` for now | [D-006](#d-006) |
| Source control | **Git monorepo** at the workspace root | [D-013](#d-013) |
| Logging | **Serilog**, config-driven; audit trail stays in the DB | [D-014](#d-014) |
| Code structure | **Vertical Slice** + the dependency rule; folders, not projects | [D-015](#d-015) |
| Session state | **LiveKit rooms**, until the queue needs history | [D-016](#d-016) |
| Service boundary | **VTM session backend**; core banking is a separate API, linked by `sessionId` | [D-017](#d-017) |
| Persistence | **SQL Server + EF Core**, dedicated `VtmSessions` database | [D-020](#d-020) |
| Authentication | **JWT resource server**; identity source is a config switch | [D-021](#d-021) |
| Ring transport | **SignalR**, grouped by branch | [D-022](#d-022) |
| Teller UI | **MUK UI Kit** (`@thimi/muk-kit`) | [D-024](#d-024) |

## 3. Component status

| Component | Status | Notes |
|---|---|---|
| LiveKit server | 🟢 **Running** | `LivekitServer/livekit-server.exe`, v1.13.7, port 7880, single-node, NSSM service |
| API — token generation | 🟢 **Working** | `POST /api/token`, kiosk + teller roles, verified end-to-end |
| API — build/publish | 🟢 **Working** | 0 warnings; ReadyToRun self-contained publish verified |
| API — logging | 🟢 **Working** | Serilog, console + daily rolling file, configured from `appsettings.json` |
| API — config validation | 🟢 **Working** | `LiveKitOptions` validated on start |
| API — health check | 🟢 **Working** | `GET /health`, real LiveKit probe, 503 when down |
| API — error handling | 🟢 **Working** | `Twirp.ErrorCode` → HTTP status |
| API — session lifecycle | 🟢 **Working** | create / list / get / status / end, persisted to SQL Server ([D-020](#d-020)) |
| API — authentication | 🟢 **Working** | JWT + policies on every endpoint; Local mode live, Oidc mode wired ([D-021](#d-021)) |
| API — queue & ring | 🟢 **Working** | `/api/queue`, accept with race handling, SignalR hub per branch ([D-022](#d-022)) |
| API — participant control | 🟢 **Working** | mute/unmute, remove, permissions, notify, transfer, monitor ([D-018](#d-018)); swept end to end in [D-019](#d-019) |
| API — kiosk command channel | 🟢 **Working** | Typed contract in `vtm-shared/`, state resync, rejoin ([D-029](#d-029)). Form and device commands are now entries in the contract; Class 2 still wants [D-011](#d-011) |
| API — webhooks | 🔴 **Not started** | `WebhookReceiver` + `livekit.yaml` config |
| API — persistence | 🟢 **Working** | SQL Server + EF Core, 10 tables in `VtmSessions` ([D-020](#d-020)) |
| API — kiosk registry | 🟢 **Working** | register / list / enrol. Secrets stored hashed; the kiosk reads its own from a gitignored file ([D-026](#d-026)) |
| Kiosk page (web) | 🟢 **Working** | `vtm-kiosk`, Angular 21 zoneless; verified in Chromium ([D-023](#d-023)) |
| Teller app (Angular) | 🟢 **Working** | `vtm-teller` on MUK UI Kit; sign in → queue → accept → call ([D-024](#d-024)) |
| API — teller management | 🔴 **Not started** | No CRUD for tellers. They exist only from the demo seed and the bootstrap admin |
| TLS | 🔴 **Not started** | Everything is `http://` and `ws://`. Mandatory before any deployment |
| Recording (egress) | ⚫ **Blocked** | Needs `livekit-egress` + Redis deployed |
| SIP fallback | ⚫ **Out of scope** | Needs `livekit-sip` |

## 4. Deliberately not doing

Recorded so nobody re-opens these without new information.

- **Desktop remote control of the kiosk** — rejected, [D-009](#d-009)
- **Per-action audit through the API** — rejected for UI/form commands, [D-010](#d-010)
- **Native .NET client for the kiosk** — rejected, [D-007](#d-007)
- **Native AOT for the API** — rejected, [D-003](#d-003)
- **Carter / plain Minimal API rewrite** — rejected, [D-004](#d-004)
- **Environment variables for secrets** — deferred by request, [D-006](#d-006)

## 5. Known issues

| # | Issue | Where | Severity |
|---|---|---|---|
| ~~K-1~~ | ✅ **Fixed** in [D-015](#d-015). Token TTL is the SDK default **6 hours**. A VTM session is minutes. | `LiveKitService` | Medium |
| ~~K-2~~ | ✅ **Fixed** in [D-015](#d-015). `LiveKit:ServerUrl` points at `https://monapisam.nipunmcs.biz` while the local server is on `:7880`. Also `_serverUrl` is loaded but never used. | `appsettings.json`, `LiveKitService` | Medium |
| ~~K-3~~ | ✅ **Fixed** in [D-015](#d-015). Teller token carries `RoomCreate`, `RoomRecord`, `Recorder` — far more authority than a browser needs. Room creation and recording should be API-side. | `LiveKitService` | **High** |
| ~~K-4~~ | ✅ **Fixed** in [D-015](#d-015). `RoomServiceClient` mutates `HttpClient.DefaultRequestHeaders` per call — **not thread-safe**. Must not be a singleton. Will bite the moment session endpoints land. | Not yet written | **High (latent)** |
| ~~K-5~~ | ✅ **Superseded** by [D-027](#d-027). No credential value sits in tracked config any more; `appsettings.json` ships every one of them empty. The exposure that already happened is K-13. | — | — |
| ~~K-6~~ | ✅ **Fixed** in [D-020](#d-020). SQL Server + EF Core, 10 tables in a dedicated `VtmSessions` database. Sessions and their timeline now survive the LiveKit room. | — | — |
| ~~K-7~~ | ✅ **Fixed** in [D-021](#d-021). Every endpoint sits behind a policy; only `/health` and the two auth endpoints are anonymous. | `Features/` — all | — |
| K-10 | No refresh tokens. A teller console open all shift outlives a 30 minute access token, and the front end has no way to renew without a fresh login. Deferred in [D-021](#d-021) rather than solved with a long-lived token. | `Features/Auth` | Medium |
| ~~K-11~~ | ✅ **Superseded** by [D-027](#d-027). The signing key and both passwords moved to the gitignored `appsettings.Development.json`; a committed template records what a clone must supply. | — | — |
| ~~K-12~~ | ✅ **Closed** on 2026-09-23. The repository history was restarted from a single commit and the old GitHub repository deleted, so the commit carrying that secret no longer exists. The secret was revoked in any case. | — | — |
| K-13 | The LiveKit key pair and `Auth:Local:SigningKey` published to GitHub in [D-027](#d-027) are **still the values in use**, and LiveKit is **reachable from the internet**, not just the LAN — both front ends connect to `wss://monapisam.nipunmcs.biz`. Confirmed exploitable: a token signed with the published key was accepted by that host (`ListRooms` → `{"rooms":[]}`), so anyone reading the repo can mint join tokens and enter a live session. Rotate the pair, the signing key, and `LivekitServer/livekit.yaml` with them. | `appsettings.Development.json`, `livekit.yaml` | **Critical** |
| K-15 | `SessionCommands` and `SessionSubmissions` exist as tables and **nothing writes to either**. `SessionSubmissions.CoreReference` is the entire link to core banking, so until something writes it there is no record tying a video session to the transaction it produced. Blocked on deciding how a customer is identified — `Session` has no customer field at all. | `Features/Sessions`, `Domain/Sessions` | **High — this is the business core** |
| ~~K-14~~ | ✅ **Fixed** in [D-030](#d-030). `LinkWatchdog` uses `navigator.onLine` (2ms) plus a media-stall check instead of waiting for the SDK. Fifteen seconds became one. | — | — |
| ~~K-9~~ | ✅ **Fixed.** `room.enable_remote_unmute: true` added to `livekit.yaml` and the service restarted; unmute verified returning 200. | `LivekitServer/livekit.yaml` | Low |
| ~~K-8~~ | ✅ **Fixed** in [D-015](#d-015). Sample scaffolding still present: `Features/Testing/GetTodos.cs`, the `Todo` record, and an empty `Domain/` folder. | API project | Low |

---

# HISTORY — APPEND ONLY

## 6. Decision log

Newest last. **Never edit an entry.** To reverse one, add a new entry referencing it.

---

### D-001
**2026-09-18 · Reviewed all three LiveKit libraries against source**

Read `Livekit.Server.Sdk.Dotnet` (v1.2.3), `Livekit.Rtc.Dotnet` (v0.1.4) and `livekit-client`
(v2.22.3) from the checkouts in this workspace rather than from memory or public docs.

**Why:** the versions here are pinned and the .NET SDKs are community-maintained; public
documentation may not match what is actually in `livekit-server-sdk-dotnet-main/`.

**Result:** `docs/01`, `docs/02`, `docs/03`.

---

### D-002
**2026-09-18 · Created `docs/` as the reference set**

Four files: an index plus one per library. Written against the local source, with VTM-specific
guidance rather than generic API listings.

---

### D-003 {#d-003}
**2026-09-18 · Native AOT turned OFF for the API**

Changed `<PublishAot>true</PublishAot>` → `false`; added `PublishReadyToRun` to the publish profile
(which already had `RuntimeIdentifier=win-x64` and `SelfContained=true`).

**Why:** a real AOT publish produced three reflection-based failure paths in
`Livekit.Server.Sdk.Dotnet`, all of which fail *silently at runtime* rather than at build time:

1. `AccessToken.ToJwt()` builds the `video` claim with `Type.GetProperties()` (IL2075) — trimmed
   grant properties produce an empty `video` claim, i.e. a token that looks valid but carries no
   permissions. Security-relevant.
2. `WebhookReceiver.Receive()` uses protobuf `MessageParser.ParseJson()` — reflection over
   descriptors. Webhooks are on the roadmap.
3. `WithRoomConfig()` uses `JsonFormatter` — same pattern.

Against that, the benefit was ~120ms of startup time on a service that runs for weeks. Notably
**FastEndpoints and FluentValidation produced zero AOT warnings** — the problem was entirely the
LiveKit SDK.

**Verified:** build 0 warnings/0 errors; ReadyToRun self-contained publish succeeded (and no longer
needs the MSVC toolchain); ran the published binary and decoded a teller token — all **17**
`video` claim properties present.

**Reversible if:** the SDK stops using reflection in `ToJwt`, or we stop using webhooks and
`RoomConfig`. `docs/01` §11 keeps the constraints and the `[DynamicDependency]` mitigation.

---

### D-004 {#d-004}
**2026-09-18 · Staying on FastEndpoints**

Considered plain Minimal API and Minimal API + Carter.

**Why FastEndpoints:** already working, validators and the error envelope already written, REPR
fits the existing `Features/` layout, and it emitted no AOT warnings. Switching was days of churn
for no functional gain.

**Why not Carter:** it solves route-module organisation, which `MapGroup` has done natively since
.NET 8. A dependency for that is hard to justify in 2026.

**Note:** with AOT off ([D-003](#d-003)), "best AOT story" stopped being a deciding criterion at
all, which removed the main argument for plain Minimal API.

---

### D-005
**2026-09-18 · Moved `AppJsonSerializerContext` into FastEndpoints' serializer options**

Removed `builder.Services.ConfigureHttpJsonOptions(...)` and registered the context on
`c.Serializer.Options.TypeInfoResolverChain` inside `UseFastEndpoints(...)`.

**Why:** FastEndpoints does **not** read ASP.NET Core's `ConfigureHttpJsonOptions` — it has its own
`Config.Serializer.Options` (`JsonSerializerOptions`). Confirmed in the FastEndpoints 8.3.0
assembly docs. The original registration affected only Minimal API endpoints, of which there are
none, so it was doing nothing while appearing to do something. Under AOT it would have failed at
runtime.

The context is kept (not deleted) because `LiveKitService` uses
`AppJsonSerializerContext.Default.ParticipantMetadata` directly, and source-gen JSON is still a
small perf win without AOT.

---

### D-006 {#d-006}
**2026-09-18 · API secret stays in `appsettings.json`**

Recommended moving `LiveKit:ApiSecret` to environment variables. **User decided to keep it in
`appsettings.json`** for now.

Tracked as known issue K-5. Revisit before this leaves the development machine.

---

### D-007 {#d-007}
**2026-09-18 · Kiosk uses WebView2 + `livekit-client`, not a native .NET client**

**Why:** there is no official LiveKit .NET client SDK. LiveKit's own SDK list marks .NET as
*(community)*, and that community SDK is `Livekit.Rtc.Dotnet` in this workspace. The only official
C# client is the Unity SDK, which is bound to Unity's rendering and audio pipeline.

`Livekit.Rtc.Dotnet` was built for *server-side* participants. It has no camera capture, no
microphone capture, no speaker playback, no video renderer, and **no echo cancellation exposed**.
You push raw frames in and pull raw frames out.

The AEC gap is the decisive one: a kiosk has a speaker and a microphone in one enclosure, so
without AEC the teller hears themselves. The native FFI layer *does* support it — the generated
protos carry `NewApmRequest` and `AudioSourceOptions` — but the managed wrapper never passes them
(`LivekitRtc/AudioSource.cs:36` leaves `Options` unset, and no C# type wraps the APM). Closing that
is upstream SDK work.

WebView2 gets camera, mic, speakers, AEC, rendering and device hot-plug from Chromium, and lets the
kiosk and teller share one client implementation.

**Three kiosk-specific WebView2 requirements** (details in `docs/02` §2):
1. `getUserMedia` needs a secure context — use `SetVirtualHostNameToFolderMapping`, not `file://`
2. Auto-grant `CoreWebView2.PermissionRequested` — nobody is at a kiosk to click *Allow*
3. Ship a **Fixed Version** runtime — Evergreen auto-updates are a risk for a deployed fleet

---

### D-008 {#d-008}
**2026-09-18 · `Livekit.Rtc.Dotnet` parked for a later feature**

Not used by the kiosk ([D-007](#d-007)). Kept in scope for a future **server-side participant** —
an auto-guide that walks the customer through a form, a transcription worker, or a recording bot.
That is exactly what the library is good at.

`docs/02` carries a status banner and will be rewritten around that feature when it is picked up.

---

### D-009 {#d-009}
**2026-09-18 · Teller controls the kiosk *application*, not the kiosk *desktop***

Considered full desktop remote control (RDP/VNC-style input injection) versus a defined
application-level command set. **Chose the command set.**

**Why not desktop control:**
- The audit trail is useless — "clicked at (847, 312)" cannot answer what a teller did, which also
  means it cannot protect a teller who is accused wrongly
- No authorisation granularity — it is all-or-nothing, so "junior teller can fill forms but not
  approve limits" becomes impossible
- Large blast radius — a compromised teller account gets a machine with a card reader, customer PII
  and bank network access
- Conflicts with [D-007](#d-007): a web app cannot inject OS-level input, so the control path would
  have to live outside the WebView
- LiveKit does not provide it. It provides screen share (one-way video) and a data/RPC channel.
  Remote control would mean bolting on a second stack

**The command set approach** turns every action into a named, loggable, authorisable operation:
`advanceStep`, `prefillField`, `triggerCardRead`, `requestSignature`, `printReceipt`,
`showDocument`, `endSession`. The blast radius is exactly the command set.

**Accepted gap:** if the kiosk app hangs, no command helps. That is an ops problem, handled by a
separate break-glass path requiring supervisor approval — not part of the everyday teller tool.

---

### D-010 {#d-010}
**2026-09-18 · Audit at submit time, not per action**

Not every command goes through the API. Commands are split:

| Class | Examples | Path |
|---|---|---|
| **1 — UI / form** | `prefillField`, `advanceStep`, `goBack`, `highlight`, `scrollTo`, `showDocument` | Peer-to-peer data channel. Not financial events; the result is visible in the submit payload. |
| **2 — side effect** | `triggerCardRead`, `printReceipt`, `captureSignature`, `openCashDrawer`, `exportDocument` | Through the API. These have real-world consequences even when no submit follows. |

**Why:** what legally matters in banking is the committed transaction, not the keystrokes leading
to it — a branch teller's keystrokes are not logged either. Routing every UI command through the
API would add latency and code for no compliance benefit. But Class 2 commands can happen in a
session that is then abandoned, leaving no submit and therefore no record, so those must be logged
regardless.

**Supporting decisions:**
- The kiosk keeps a **local command log** and posts it with the submit in one request — full
  forensics, zero per-command latency.
- Commands needing a result use **RPC**, not fire-and-forget `publishData`.
- The **session record** (`room ↔ kiosk ↔ teller`) is created server-side at session start. That is
  what makes "who did this" provable at submit without trusting the kiosk's own claim.

---

### D-011 {#d-011}
**2026-09-18 · Found two Twirp RPCs the SDK does not wrap**

Compared the Twirp route table against the service client wrappers:

- **`Twirp.PerformRpc`** (`twirp/livekit.RoomService/PerformRpc`) — fields `Room`,
  `DestinationIdentity`, `Method`, `Payload`, `ResponseTimeoutMs`. **Not exposed on
  `RoomServiceClient`.**
- `Twirp.StartEgress` — the unified egress RPC. Not exposed on `EgressServiceClient`. Not important.

`PerformRpc` matters: it lets the **API server** invoke an RPC method on the kiosk and get a
response back, which is precisely the Class 2 command path from [D-010](#d-010) —
authorise, audit, execute, and record the actual result rather than just the attempt. Better than
`SendData`, which is fire-and-forget.

Call `Twirp.PerformRpc(httpClient, request)` directly with a `RoomAdmin`-scoped bearer token.

---

### D-012
**2026-09-18 · API surface planned**

Full inventory of what the SDK can do, mapped to endpoints — see `docs/04-api-surface.md`.

Two things worth flagging from it:

- **`MoveParticipant` and `ForwardParticipant` fit VTM directly.** `Move` transfers a customer to
  another teller's room; `Forward` mirrors a session to a supervisor without joining it. Both are
  commonly overlooked and both are things a VTM needs.
- **Queue and ring logic is not a LiveKit feature.** LiveKit starts once a teller accepts. The
  waiting list, the ring-out to available tellers, and the assignment are our own API + database +
  push channel (SignalR or SSE) — the teller is not in a room yet, so LiveKit cannot carry that
  notification.

---

### D-013
**2026-09-18 · `git init` at the workspace root, monorepo layout**

Repository root is `D:\MCS\VTM\LIvekit`. Branch `main`.

**Tracked:** `LivekitServerAPI/`, `docs/`, `CLAUDE.md`, `.gitignore`,
`LivekitServer/livekit.yaml`.

**Ignored:** the two third-party SDK checkouts (`livekit-server-sdk-dotnet-main/`,
`client-sdk-js-main/`), `setup-zip/`, the `livekit-server.exe` binary and NSSM, and the usual
.NET / Node build output. The Node and Angular rules are already in place so the teller app and
the kiosk app can be added at the root without touching `.gitignore` again.

**Why a monorepo:** the API, the kiosk and the teller are one system with one decision log. Keeping
them together means a change that spans the API and a client is one commit, and PROJECT.md stays
the single history for all of it.

`livekit.yaml` is tracked (deployment config worth versioning) while the 56 MB binary is not.

**Note:** `appsettings.json` is tracked and contains the LiveKit API secret — see
[D-006](#d-006) / K-5. Git history is permanent, so if a remote is ever added, rotate the secret
and move it out *before* the first push rather than after.

---

### D-014
**2026-09-18 · Replaced the hand-rolled file logger with Serilog**

Removed `Infrastructure/Logging/FileLoggerProvider.cs` and the
`ClearProviders()` / `AddConsole()` / `AddFile()` calls. Added `Serilog.AspNetCore` 10.0.0 with the
Console and File sinks, configured entirely from the `Serilog` section of `appsettings.json`.

**Why the old one had to go:**
1. **No rolling** — `app.log` grew without limit.
2. **`File.AppendAllText` per line** — opened, wrote and closed the file for every entry, with a
   `lock` funnelling every request thread through it. A real bottleneck once several tellers are
   active.
3. **No structured logging** — `formatter(state, exception)` flattened everything to a string, so
   `LogInformation("Teller {TellerId} ...", id)` lost its properties and the log could only be
   grepped, never queried.

Minor: `IsEnabled` always returned true, no `[ProviderAlias]` so it could not be configured
per-provider, `BeginScope` returned null so no correlation context, and `DateTime.Now` carried no
offset.

**Why Serilog specifically:** structured properties matter here because of the session/audit work
in [D-010](#d-010) — `{RoomName}`, `{TellerId}`, `{SessionId}` stay queryable. `Enrich.FromLogContext`
lets a session id be pushed once per request and appear on every subsequent line.
`UseSerilogRequestLogging()` collapses the framework's per-request noise into one line with a
duration. Sinks for Seq, Elasticsearch or a database are config changes, not code changes.

Not a concern because AOT is off ([D-003](#d-003)); Serilog's reflection would need weighing if that
ever changed.

**Log paths:** `C:\ProgramData\VTM\logspi-.log` in Production (daily roll, 50 MB cap, 31 files
retained), `logs/api-.log` in Development. Production deliberately writes outside the publish folder
so a redeploy does not wipe history — the NSSM service account needs write permission there.

**Logs are not the audit trail.** Serilog is for diagnostics. The submit record and Class 2 command
results from [D-010](#d-010) belong in the database: log files roll, get deleted, and are not
transactional. Do not conflate the two.

**Verified:** build 0 warnings/0 errors; ran the published binary in Production mode; the rolling
file `api-20260918.log` was created with timestamps carrying offsets and `SourceContext`;
`POST /api/token` returned 200 and the validation path 400, each producing a single
`RequestLoggingMiddleware` line with elapsed time; `Microsoft.AspNetCore` noise correctly suppressed
by the level override.

---

### D-015 {#d-015}
**2026-09-18 · Step 1 foundation: service split, config validation, role grants**

Settled the architecture question first: **Vertical Slice, with Clean Architecture's dependency
rule and none of its ceremony.** `Features/` and `Infrastructure/` may depend on `Domain/`;
`Domain/` depends on nothing. Folders, not four projects — the domain here is orchestration, not an
invariant-rich model, so the layering would have cost more than it returned. Revisit if the domain
logic deepens or business rules need testing without infrastructure.

```
Domain/         Sessions/ ParticipantRole, ParticipantMetadata      (no dependencies)
Contracts/      ApiResponse<T>, ErrorResponseDto
Features/       Tokens/ Health/                                     (one slice per folder)
Infrastructure/ LiveKit/ Errors/
```

**Replaced `ILiveKitService`** with two focused services:
- `ITokenService` → **singleton**, stateless.
- `IRoomService` → **scoped**, built on `IHttpClientFactory`. Fixes **K-4** before any endpoint
  could depend on the broken lifetime.

**`LiveKitOptions` + `ValidateOnStart`** — the boot now fails, loudly, on a missing secret, a
secret under 32 bytes, a `ws://` management URL, or a nonsensical TTL. Fixes **K-2**.

**Role → grants is now one `switch` in `TokenService`** and the only place authority is expressed.
Fixes **K-1** (TTL 6h → 15min, configurable) and **K-3** (`RoomCreate`, `RoomRecord` and `Recorder`
removed from the teller). Added `Supervisor` as a real role — `Hidden`, subscribe-only.

Also: `LiveKitExceptionHandler` maps `Twirp.ErrorCode` and `HttpRequestException` onto sensible
status codes instead of a bare 500; `GET /health` actually calls LiveKit and returns 503 when it is
unreachable; sample scaffolding removed (**K-8**).

**Verified against the running LiveKit server:**

| Check | Result |
|---|---|
| `GET /health` | 200, `liveKit: "up"`, and the server logged the `RoomService.ListRooms` call |
| `GET /health` with LiveKit down | 503, `liveKit: "down"` |
| Kiosk / Teller / Supervisor grants | `roomCreate`, `roomRecord`, `recorder` **false for all three**; `roomAdmin` only on teller; `hidden` only on supervisor; supervisor `canPublish` false |
| Token TTL | 900s for all roles |
| Invalid role | 400 with the custom envelope |
| `ws://` ServerUrl | boot refused with the explanatory message |
| Secret under 32 bytes | boot refused |
| `GET /todos` | 404 — sample endpoint gone |

**Still open:** K-5 (secret in `appsettings.json`), K-6 (no persistence), **K-7 (the token endpoint
is still `AllowAnonymous`)**.

---

### D-016 {#d-016}
**2026-09-18 · Step 2: session lifecycle endpoints**

```
POST   /api/sessions                  create room + return the kiosk token together
GET    /api/sessions                  active sessions (supervisor dashboard)
GET    /api/sessions/{room}           session + participants
PATCH  /api/sessions/{room}/status    waiting -> active -> ending
DELETE /api/sessions/{room}           end the session
```

**No database, deliberately.** LiveKit rooms *are* the session state at this stage, so
`ListRooms` answers "what is live right now". This let the whole control plane be proven
end to end before committing to a persistence stack (K-6). It stops being enough once the
queue needs history and the audit trail needs to survive a room being deleted.

**Room names are generated server-side** (`vtm-` + 12 hex), never supplied by the caller. A
client that can name its own room can ask for a token into somebody else's session. The
create response returns the room and its kiosk token together so the kiosk needs one round
trip.

**`MaxSessionParticipants` defaults to 3, not 2.** Kiosk + teller + one *hidden* supervisor.
Two would silently lock supervisors out of the `Hidden` role added in [D-015](#d-015). It is
still a cheap guard: a leaked token cannot let a fourth party in.

**Status lives in room metadata**, so changing it broadcasts `RoomMetadataChanged` to every
participant and the kiosk and teller UIs react without polling this API.

`SessionMapping` tolerates rooms this API did not create — unparseable metadata and unknown
identity prefixes degrade to defaults rather than throwing, since agents and egress
participants will appear in these lists later.

**Verified against the running LiveKit server** — full lifecycle plus all 11 new
`.http` cases:

| Check | Result |
|---|---|
| Create | 201, room `vtm-3e2d023fbcbf`, sid `RM_ogDvERqLWVk9`, status `Waiting` |
| Token binding | the token's `video.room` equals the generated room name; `roomJoin` true, `roomAdmin`/`roomCreate` false, 900s TTL |
| `MaxParticipants` | server log confirms `max 3 participants` |
| List / detail | room appears with kiosk id, branch and participant count |
| Status change | `Waiting -> Active`, and re-reading the session shows it |
| Delete | 200; subsequent GET and DELETE both 404 |
| Validation | empty kiosk id 400, invalid status 400, unknown room 404 on both detail and status |

Also fixed a cosmetic logging inconsistency — an enum passed straight into a Serilog
template rendered quoted (`Waiting -> "Active"`) while the adjacent string did not.

---

### D-017 {#d-017}
**2026-09-18 · Service boundary: this API is the VTM session backend, not the bank's backend**

The VTM system is **two backends**. This one owns everything that exists *because VTM exists*:
sessions, queue, kiosks, teller availability, LiveKit rooms and tokens, the command log and the
session audit. The bank's core system owns customers, KYC, accounts, transactions and the ledger.

**The dividing question is "does this exist without VTM?"**, not "is this LiveKit?". The queue has
nothing to do with LiveKit, but a queue entry is meaningless without a session, so it lives here.
Drawing the line at the technology would have put it in the wrong place.

**One service, not two, for the VTM side.** Creating a session is a room *and* a database row;
a teller accepting is a queue update *and* a token. Splitting those would mean a two-phase commit
and a network hop per token, for no benefit. The vertical slices from [D-015](#d-015) leave the
seams in place if it ever does need splitting.

**Clients call both APIs directly — this API never proxies core banking.** Proxying would put
customer PII through a service with no need to see it, couple every core change to a passthrough
change here, and add a hop and a failure mode. *Exception:* if the bank's core is unreachable from
a browser, a gateway is needed — but as a separate component. Giving this API that second job
recreates exactly the problem the boundary prevents.

**The two are linked by `sessionId`** (the room name), which the client sends to the core API with
the operation. The linkage then lives in the authoritative record rather than depending on a client
remembering to report it afterwards. `SessionSubmissions` here stores the core's reference and
nothing else — no amounts, no account numbers.

**Name kept as `LivekitServerAPI`** despite the broader scope; renaming is churn without benefit.
Worth knowing that the contents are wider than the name suggests.

Schema and the full operation inventory: `docs/05-data-model.md`.

---

### D-018 {#d-018}
**2026-09-18 · Participant control, and three things the self-hosted server taught us**

Added the in-call controls a teller needs:

```
POST   /api/sessions/{room}/participants/{id}/mute
DELETE /api/sessions/{room}/participants/{id}
PATCH  /api/sessions/{room}/participants/{id}      permissions
POST   /api/sessions/{room}/notify                 SendData
POST   /api/sessions/{room}/transfer               hand to another teller
POST   /api/sessions/{room}/monitor                hidden supervisor token
```

Verified with a **real participant**: a headless client built against `Livekit.Rtc.Dotnet` (the
parked library from [D-008](#d-008)) that joins a room and publishes an audio track. Testing mute
and permissions against an empty room would have proved nothing.

Three findings, none of which were visible without running it:

**1. `MoveParticipant` and `ForwardParticipant` are not implemented on the self-hosted server.**
Both return `twirp error unknown: not implemented` on LiveKit OSS v1.13.7. They are in the SDK and
in the protocol, but they are Cloud features. The supervisor-monitoring and transfer designs
sketched in [D-012](#d-012) depended on them.

Reworked, and both are better for it:

- **Monitor** now issues a **hidden supervisor token for the same room** instead of mirroring
  tracks into an observer room. Simpler, and `MaxSessionParticipants = 3` from [D-016](#d-016)
  already reserved the seat.
- **Transfer** now **swaps the teller inside the room** rather than moving the customer out of it.
  The customer's connection, tracks and screen share are untouched. Moving the *customer* for a
  change that has nothing to do with them was the wrong shape anyway.

`MoveParticipantAsync` and `ForwardParticipantAsync` were removed from `IRoomService` rather than
left as dead code.

**2. Server-side unmute is refused by default.** `MutePublishedTrack` mutes happily but returns
`remote unmute not enabled` when unmuting. Needs `room.enable_remote_unmute: true` in
`livekit.yaml` — added, but the service restart requires elevation and is still pending, so unmute
is the one path not yet confirmed working.

**3. Error responses were PascalCase while success responses were camelCase.** `LiveKitExceptionHandler`
serialises through `AppJsonSerializerContext` directly, which had no naming policy, while
FastEndpoints applies camelCase through its own options. Any client parsing both shapes would have
broken on errors. Fixed with `[JsonSourceGenerationOptions(PropertyNamingPolicy = CamelCase)]` on
the context, so both paths agree.

Also: `UpdateParticipantPermissionsAsync` clones the participant's existing `ParticipantPermission`
before changing two fields. Building a fresh one would have silently cleared `CanSubscribe`,
`Hidden` and `CanPublishSources` — a "read-only customer" would have gone deaf and blind as well
as mute.

**Verified against the running server:** mute (1 track affected), permissions revoked, notify both
targeted and broadcast, monitor token `hidden=true` / `canPublish=false` / bound to the right room,
transfer removing `teller-T-07` while `kiosk-K-01` stayed and `teller-T-09` got a room-scoped admin
token, and 404 on every control path once the participant had left.

---

### D-019
**2026-09-18 · Full endpoint sweep; two fixes**

Ran every endpoint against the live LiveKit server with two real participants joined — 40 cases
across health, tokens, sessions, participant control and regression paths. All pass.

**K-9 resolved.** The `LivekitServer` service was restarted with
`room.enable_remote_unmute: true`, and server-side unmute now returns 200 instead of 412.

**Mute with an unknown `trackSid` used to report success.** LiveKit's `MutePublishedTrack`
accepts a track sid that does not exist and returns 200, so passing it straight through meant the
API answered `tracksAffected: 1` when nothing had been muted. A caller had no way to tell a
successful mute from a typo. The endpoint now checks the sid against the participant's track list
first and returns 404 if it is not there. Muting *all* tracks (no `trackSid`) is unaffected — it
derives the list from the participant.

The full-sweep results are recorded in `LivekitServerAPI.http`, which now carries the expected
status against every request.

---

### D-020 {#d-020}
**2026-09-21 · Persistence: SQL Server + EF Core (K-6)**

Ten tables, one migration, applied and verified. Schema follows `docs/05-data-model.md`.

**SQL Server + EF Core**, as recommended: the bank runs Windows with a default MSSQL instance
already up, and the schema will keep moving, so migrations earn their keep. Entities are plain
POCOs in `Domain/` with no EF attributes; mapping lives in `Infrastructure/Persistence/Configurations`
via `IEntityTypeConfiguration`. That keeps the dependency rule from [D-015](#d-015) intact —
`Domain/` still references nothing.

**Enums are stored as strings.** A DBA reading this schema during an audit should see `Abandoned`,
not `4`, and reordering an enum must never silently rewrite the meaning of existing rows.

**Delete behaviour is deliberate, not default.** `SessionEvents` cascades — a timeline is
meaningless without its session. `SessionCommands` and `SessionSubmissions` **restrict**: a card
read or a print happened in the real world, and a submission points at a real transaction.
Deleting a session must not quietly erase either. `Kiosks` restricts too, so a device cannot be
removed out from under its own history.

**Branch now comes from the kiosk record, not the request.** `CreateSessionRequest` lost its
`BranchId`. A kiosk asserting its own branch could have claimed to be somewhere it is not, and the
value feeds branch-level reporting and routing.

**Sessions require a registered kiosk.** The FK enforces it and the endpoint returns 404 for an
unknown kiosk, 409 for one that is not `Active`. Added `POST /api/kiosks` and `GET /api/kiosks` to
make that possible — registration only; enrolment and credentials come with K-7. Re-registering
updates the row rather than failing, because a kiosk being renamed or moved between branches is
routine and its session history has to survive it.

**Orphaned-room guard.** If persisting fails after the LiveKit room is created, the room is
deleted rather than left for the empty-timeout to collect.

**Two problems found on the way:**

1. **`InvariantGlobalization=true` breaks `Microsoft.Data.SqlClient`** — it throws
   `Globalization Invariant Mode is not supported` at startup. The setting was only ever there for
   AOT trimming, which is off ([D-003](#d-003)). Now `false`.

2. **A database named `Vtm` already existed** on this instance, holding someone else's schema
   (`MCLIENT_HEADER`, `MACCT`, `MACCT_TYPE`, `MDISTRICT`, `MTITLE`, and an EF6-era
   `__MigrationHistory`). The first migration ran straight into it. Moved to a dedicated
   **`VtmSessions`** database and dropped the ten tables plus `__EFMigrationsHistory` that had been
   added to `Vtm`, leaving it exactly as found. Nothing was read from those tables.

   This is [D-017](#d-017) in physical form: VTM session state and core banking data must not share
   a database. `VtmSessions` is the name to keep.

**Verified:** kiosk register 201 / re-register 200 / missing branch 400; session against an
unregistered kiosk 404, against a registered one 201 with the branch resolved from the kiosk row;
status and end persisted. `SELECT` over the database shows `Sessions` carrying
`Status`/`EndReason`/`WaitSeconds`/`DurationSeconds`, and `SessionEvents` holding the append-only
timeline — `session.created`, then `{"from":"Waiting","to":"Active"}`, then
`{"from":"Active","to":"Ended"}`.

A session ended with no teller ever accepted is recorded as **`Abandoned` / `CustomerLeft`**, not
`Ended` / `Completed`. Abandonment rate is a number this system will be judged on; it should not
depend on remembering to distinguish the two later.

---

### D-021 {#d-021}
**2026-09-21 · Auth, Phase 1 (K-7) — identity source kept swappable**

Every endpoint is now behind a policy. The bank has not decided how tellers will log in, so the
API was built so that decision can arrive later without touching a single endpoint.

**The API is a JWT bearer resource server, and nothing more.** It validates a token and reads
claims. *Who issued the token* is one config value:

```
Auth:Mode = "Local"   → this API authenticates and signs its own tokens
Auth:Mode = "Oidc"    → Entra/Keycloak issues them; we validate via JWKS
```

Under OIDC, the provider's groups are mapped onto our roles in **one place**, a
`JwtBearerEvents.OnTokenValidated` handler. **Endpoints never see an AD group name.** That is the
whole of what keeps the identity source swappable — the moment a group name leaks into an
endpoint, it stops being a config change.

**On-prem AD does not change the design either.** LDAP issues no JWT, so the login endpoint would
bind against LDAP instead of comparing a hash and still sign our own token. That is one
implementation of `IUserDirectory`, nothing more.

**Claims contract, ours:** `sub`, `role`, `branch`, plus `kiosk_id` on device tokens.
**Policies:** `Kiosk`, `Teller` (teller + supervisor), `Supervisor`, `Admin`, `Staff`.

**Two JWTs now exist and confusing them would be expensive**, so `ITokenService` was renamed
`ILiveKitTokenService` and the new one is `IApiTokenIssuer`. An API token says who is calling and
is the *input* to deciding which LiveKit token they may have.

**Kiosks authenticate as devices, not people.** `POST /api/kiosks/{id}/enroll` generates 32 bytes
of CSPRNG output, returns it **once**, and stores only a hash. There is no way to read it back.
Rotation leaves the old credential live by default — a kiosk mid-session should not be cut off
because someone pre-generated its next secret — and `revokeExisting` kills it when a device is
lost. The token's branch and kiosk id come from the enrolled record, so a kiosk cannot claim to be
a different device or to be somewhere else.

**Login failures are deliberately uniform.** A missing account still runs a hash so it takes the
same time as a wrong password, and the message never says which was wrong. Response timing is an
account-enumeration oracle otherwise.

**Bootstrap admin** is seeded from config on first run, only under Local mode, only when a
password is set, and **never over an existing account** — re-running must not reinstate a password
someone has since changed.

**One bug worth remembering:** every policy returned 403 for a token that plainly carried
`role=admin`. The JWT handler silently rewrites `role` and `sub` into the long WS-Federation URIs
under its default inbound claim map, so `RequireClaim("role", ...)` never matched. Fixed with
`MapInboundClaims = false` in both modes. It presents as a policy bug and is not one.

**Verified end to end:** every endpoint 401 without a token and `/health` still open; login 401 on
bad password, unknown user and 400 on empty; admin can register, enroll and rotate; a rotated
secret is rejected; a kiosk token can create a session and is refused everything else
(sessions list, kiosk list, token mint, enrolling itself); an admin token is refused the
teller-only and supervisor-only endpoints while passing the Staff ones; malformed and tampered
tokens 401.

**Not done, deliberately:** refresh tokens. A teller console open all shift will outlive a 30
minute access token. Deferred rather than solved with a long-lived token that cannot be revoked.

---

### D-022 {#d-022}
**2026-09-21 · Queue and ring; demo scope agreed**

**Scope call: build a working demo before building more backend.** A lot is now implemented and
nothing is visible end to end. User management is deferred — it is admin plumbing and moves the
demo no closer. What the demo has to prove is the scenario: a customer arrives, tellers hear it,
one accepts, the call runs.

**The kiosk is a web page for the demo, not a WPF app.** [D-007](#d-007) already decided the kiosk
is WebView2 hosting `livekit-client`, so the WPF shell only ever hosts that page. Building the page
first removes WPF from the critical path entirely, and the shell added later changes nothing about
it. WPF becomes necessary when card readers and printers do.

**SignalR for the ring**, not SSE. The teller is not in a LiveKit room yet — that is the whole
point of a queue — so LiveKit cannot carry this notification. SSE would work, but a teller console
that misses a ring is a customer left standing, and SignalR handles reconnection rather than
leaving it to be hand-rolled.

```
POST /api/sessions                 (existing) creates Waiting and rings the branch
GET  /api/queue                    who is waiting, oldest first
POST /api/queue/{room}/accept      teller takes it, gets their token
GET  /api/me                       who the caller is
PATCH /api/me/status               Available / Away - decides who the ring reaches
```

Hub at `/hubs/queue`, tellers grouped by branch (`branch:{id}`), so a customer in Colombo does not
ring a desk in Kandy. Supervisors and admins may watch other branches. Server-to-client calls are
typed through `IQueueClient`, so a renamed method is a compile error rather than a ring that
quietly stops arriving.

**A browser cannot set an Authorization header on a WebSocket handshake**, so SignalR passes the
token in the query string. Accepted only for `/hubs` paths — a token in a URL must never be valid
for a normal API call, where it would land in access logs.

**Losing the accept race is normal, not exceptional.** Two tellers watching the same queue will
press at the same moment. The loser gets 409 and a message that distinguishes "another teller took
this" from "you already have it", and `SessionTaken` clears it from every other list.

**Demo data is seeded behind `DemoData:Enabled`**, default off: two tellers, a supervisor and kiosk
K-01. Scaffolding, not a feature — real teller administration is still to come.

**One more casing bug, same class as [D-018](#d-018)'s.** FluentValidation and
`AddError(x => x.Prop)` disagree on key casing, so the same response envelope carried `roomName`
from a validator and `RoomName` from a handler. A client keying off one would silently miss the
other. Keys are now camelCased in the response builder, which is the one place that covers every
source.

**Verified with a real SignalR client** connected as `teller1`: the ring arrived on session
create, `SessionTaken` on accept, `SessionEnded` on delete; the second teller got 409 on the same
session and the first got a different 409 for re-accepting; queue counts and the teller token's
`room` claim all correct.

---

### D-023 {#d-023}
**2026-09-21 · Kiosk page — the first thing a customer actually sees**

`vtm-kiosk`, Angular 21, zoneless. Four states: idle, waiting, in call, ended.

**The call starts when the teller joins, not when their camera arrives.** The first version
switched screens on `TrackSubscribed` for a video track, which meant a teller with their camera off
left the customer staring at "waiting" while someone was already talking to them. Now
`ParticipantConnected` switches the screen and a camera-off placeholder stands in for the video.
That is also the correct behaviour, not just the testable one.

**No `NgZone.run()` anywhere.** LiveKit fires its callbacks from outside Angular, and
[doc 03 §6](03-livekit-client-js.md#6-an-angular-service) recommends `NgZone` for that — written
before the app existed. Under `provideZonelessChangeDetection()` a signal write schedules a render
by itself, so the zone wrapping is dead weight. Doc 03 needs correcting.

**The kiosk authenticates itself on power-up.** Nobody is there to log it in: `ngOnInit` exchanges
the device secret for an API token ([D-021](#d-021)). A kiosk that has not been enrolled says so
rather than showing a Start button that cannot work.

**`endSession` does not let a failed request trap the customer.** Deleting the room is best-effort;
a customer walking away must not be blocked by the API being slow. The empty-timeout collects
anything left behind.

**Verified in a real browser** — Chromium with a fake camera and mic, driven end to end while a
teller joined from outside:

| Step | Result |
|---|---|
| Boot | Start button appeared, so device auth succeeded |
| Start | "Waiting for a teller…", local preview **1280x720 playing** |
| Teller joins | screen switched, name "teller1" shown, camera-off placeholder rendered |
| Own video | picture-in-picture **1280x720 playing** |
| Mute | button flipped to "Unmute" |
| Camera | button flipped to "Show video" |
| End | back to idle |

No console errors.

**Two problems found on the way, neither in the app:** the first run measured video dimensions
before metadata had loaded and read 0x0 — a test artifact, now waited for. And the .NET probe from
[D-018](#d-018) had lost `Google.Protobuf` when the NuGet cache was cleared, and needed a rebuild.

**Disk: C: reached 0 GB free mid-task**, which is what `ng new` failed on. Cleared by the user.
Worth moving the npm and NuGet caches off C: — they are ~10 GB and C: is chronically full.

---

### D-024 {#d-024}
**2026-09-21 · Teller console — the demo runs end to end**

`vtm-teller`, Angular 21, zoneless, built on **MUK UI Kit** (`@thimi/muk-kit` 1.0.1) — the kit
carved out of the bank's own application, so the console looks like the rest of the estate rather
than like a prototype. Sign in → queue → accept → call.

**The ring arrives over SignalR and the header says whether that feed is up.** That badge is not
decoration: a teller whose connection has dropped silently stops being rung, and would have no way
to tell. `withAutomaticReconnect`, and a full queue resync on `onreconnected` because anything that
happened while disconnected was missed.

**The queue list is updated from the ring payload, not by refetching.** A round trip on every
arrival is visible as a delay, and the ring is the one thing that has to feel instant.

**Losing the accept race is handled as normal, not as an error** — a warning toast and the row
disappears. Two tellers watching the same queue will press at the same moment.

**The kiosk screen takes the large tile when shared**, and the customer's face moves to the side.
That is what the teller is being asked to look at. Screen share and camera are both video tracks;
telling them apart by `Track.Source` is what makes the layout correct.

**Found by running it:** the SignalR ring payload carried the kiosk **id** but not its **name**, so
a teller saw `K-01` instead of `Colombo Main Lobby 1` until something forced a refetch —
`GET /api/queue` had always joined for the name, the notification never did. Fixed by adding
`KioskName` to `QueueEntryNotification`. A teller needs to know which desk to look at, and a code
does not tell them.

**Verified: the whole scenario, in two real browsers with fake cameras and mics** — 19 steps, all
passing, no console errors.

| | |
|---|---|
| Teller signs in | console reached, SignalR **Connected**, queue empty |
| Customer arrives | kiosk device-authenticated, waiting, local preview 1280x720 |
| Ring | reached the teller **without a refresh**, row reads "Colombo Main Lobby 1 · K-01 · 3s" |
| Accept | both sides switched to the call |
| Media | teller sees the customer, kiosk sees the teller, kiosk sees itself — all playing |
| Controls | teller mute, **mute-customer through the API**, kiosk mute |
| End | confirm dialog, teller back at the queue, kiosk back at idle |

**Two environment problems, neither in the code.** `ng new` failed on a full C: drive, and later the
machine ran out of memory: 7.7 GB total with two `ng serve` watchers, the API, LiveKit and two
Playwright browsers gave `fork: Resource temporarily unavailable` and a V8 heap abort. Building both
apps and serving the output from **one** small static process fixed it. Worth remembering — this
machine cannot run two Angular dev servers and a browser test at once.

**Bundle budget raised to 1.5 MB.** `livekit-client` + `@microsoft/signalr` + the kit are ~1 MB
before any application code; the Angular default was written for an app with no media stack in it.

**`@ng-icons` would not install** against Angular 21 (peer conflict). Not needed — the console uses
`muk-avatar` initials instead of icons.

---

### D-025 {#d-025}
**2026-09-21 · Audio was never playing — `attach()` with no element**

Reported from a manual test: video worked, audio did not, on both sides.

**The cause.** Both apps called `track.attach()` with no argument for remote audio, which
[doc 03 §4](03-livekit-client-js.md#4-rendering-remote-media) presents as the normal pattern for
audio — "you do not need an element in your template". Reading the pinned SDK source
(`client-sdk-js-main/src/room/track/Track.ts`) shows what that actually does: it calls
`document.createElement('audio')` and **never appends it**, then calls `play()` on the orphan. The
measurement agreed — **zero `<audio>` elements in the document** on both sides.

A detached media element is at the browser's mercy: Chrome may suspend it, Safari will not play one
at all, and nothing can reach it afterwards to set volume or an output device. Audio silently not
working is the worst failure this system has — the call is pointless without it — so it does not get
to depend on that.

**The fix.** A small `AudioSink` component in each app owns a real `<audio>` element in the
document and attaches the track to it, mirroring how `VideoTile` handles video. Hidden by size
rather than `display: none`, which some browsers treat as licence to stop playback. The services
now hold the track in a signal and let the component attach and detach it, so nothing attaches
blindly any more.

**Verified by measuring sound, not by reading labels.** Chrome's fake capture device emits a tone,
so a WebAudio analyser on the receiving side gives real energy:

| | Before | After |
|---|---|---|
| `<audio>` elements in the DOM | **0** | 1 per side |
| Kiosk hears the teller | unmeasurable | **RMS 0.302** |
| Teller hears the customer | unmeasurable | **RMS 0.309** |

And the three mutes, each measured at the far end:

| Action | Far-side RMS |
|---|---|
| Customer presses Mute | **0** (teller still audible to the kiosk) |
| Customer unmutes | 0.300 |
| Teller presses Mute me | **0** |
| Teller unmutes | audible again |
| Teller presses Mute customer (server-side, via the API) | **0** |

**Doc 03 §4 needs correcting** — it repeats the SDK's own advice, and that advice produces an
element nothing owns.

**The mute buttons were never missing.** Screenshots of both apps mid-call show them on each side —
Mute / Hide video / Share screen / End on the kiosk, Mute me / Hide my video / Mute customer / End
session on the teller. They looked inert because there was no audio for them to change.

---

### D-026 {#d-026}
**2026-09-22 · A kiosk secret reached a commit — the file is the wrong place for it**

While committing [D-025](#d-025) a `git add -A` swept up `vtm-kiosk/src/app/kiosk.config.ts` with a
live enrolment secret in it. My own mistake, and the second time the same file had to be scrubbed by
hand before a commit — which is the real finding. A credential that has to be manually removed
before every commit will eventually be committed.

**Done about it:**

1. **Rotated.** `POST /api/kiosks/K-01/enroll` with `revokeExisting: true`. Verified both ends —
   the committed secret now returns `401 Invalid kiosk credentials`, the new one issues a token.
   The string in history is dead.
2. **Moved out of tracked source.** `kiosk.config.ts` no longer has a `deviceSecret` field at all.
   `readDeviceSecret()` fetches `public/kiosk.secret` at boot, and that path is gitignored. Enrolling
   a kiosk can no longer put a credential anywhere git will look.
3. A dev server answers a missing file with `index.html` and a 200, so a response starting with `<`
   is treated as "not enrolled" rather than sent to the API as a secret.

**Not rewritten.** The dead string stays in history. Rewriting a shared branch to remove a revoked
demo credential on a local machine costs more than it buys. If this repo is ever pushed somewhere
that matters, that is the moment to rewrite — noted in §5 as **K-12**.

Re-verified afterwards: the kiosk enrols from the file and the full audio test passes unchanged —
RMS 0.310 / 0.297, all three mutes measured to zero at the far end.

A real kiosk still does not use a file. It receives its secret once at enrolment and keeps it under
DPAPI or the TPM ([D-021](#d-021)).

---

### D-027 {#d-027}
**2026-09-22 · Secrets out of tracked config — and what "config" actually means**

The repository was pushed to **public GitHub** (`Thimeee/livekit-kosk-gateway`) as a squashed
initial commit. That published the dev credentials that [K-5](#5-known-issues) and K-11 had been
tracking as a deployment risk: `LiveKit:ApiKey` / `ApiSecret`, `Auth:Local:SigningKey`, and the
bootstrap admin and demo passwords.

**What the exposure actually is.** The API binds `::1` — loopback only — so the signing key and the
passwords are not remotely reachable. LiveKit binds `::` and answers on `192.168.1.190:7880`, so
**anyone on the LAN holding the published key can mint tokens and join rooms**. The host is behind
NAT (egress `14.1.78.28`), so this is a LAN exposure, not an internet one, absent a port forward.

> **Corrected the same day — this paragraph understated it.** See [D-029](#d-029): the front ends do
> not use the LAN address at all. They connect to `wss://monapisam.nipunmcs.biz`, and the published
> key was confirmed working against that public host. The exposure is internet-wide.
The values are placeholder-grade and say so in their own names, but they are now burned and can
never be carried into a deployment.

**The split that matters: config is committed, credentials are not.** The instinct that "config
shouldn't go to GitHub" is half right, and the wrong half is the expensive one — a repo with no
committed config cannot be cloned and run, because nothing records what keys exist or what shape
they take. So:

| File | Committed? | Holds |
|---|---|---|
| `appsettings.json` | yes | Structure and non-secret defaults. **Every credential value is empty.** |
| `appsettings.Development.json` | **no** — gitignored | The real local values |
| `appsettings.Development.template.json` | yes | Same shape, no values, with notes on what each one needs |

The template exists because a gitignored config file solves the leak and creates a new problem: a
fresh clone has no idea what it is supposed to supply. Environment variables and
`dotnet user-secrets` keep working unchanged — ASP.NET Core reads both without any code change.

**An empty credential fails the boot, by name.** Verified by removing the dev file and starting the
API:

```
OptionsValidationException: DataAnnotation validation failed for 'LiveKitOptions'
members: 'ApiKey' ... 'ApiSecret' ...; LiveKit:ApiSecret must be at least 32 bytes
```

That is the property that makes this safe: emptying a value in a committed file cannot produce a
half-working server, only a refusal to start.

**Found on the way: CORS never allowed the kiosk.** `Cors:Origins` listed `http://localhost:4200`
(the teller) but not `:4201` (the kiosk), so the kiosk's device auth was blocked the moment the API
was restarted from clean config. It fails as a *silent* CORS block, not as anything the API logs —
worth the note in the template that both ports are required.

Re-verified after all of it: full audio test unchanged — RMS 0.313 / 0.310, all three mutes measured
to zero at the far end.

**Not done, and worth doing:** the published values are still the ones in use. Moving them out of
tracked source stops the *next* leak; it does not invalidate the one that happened. Rotating the
LiveKit key pair (also `LivekitServer/livekit.yaml`) and the signing key is a separate step. Tracked
as **K-13**.

**History was left as it is** — the user's call. The squashed initial commit stands; the older
commits remain reachable locally through the reflog, and their reasoning is here in this log
regardless.

---

### D-028 {#d-028}
**2026-09-22 · The kiosk is a terminal, not a participant — the teller holds every control**

By request, and it is the right model. Nobody is standing at a kiosk to manage a call: asking a
customer to mute themselves, or to decide whether to share their screen, is asking them to do a
job they did not come to do.

**The kiosk call screen now has one control.** "I'm finished" — which *asks*. It does not hang up.
The teller may be part way through something, and ending their call from the kiosk side is the
kiosk's decision to make least of all. The button then says the teller has been told, so the
customer knows it landed.

Mute, camera and screen-share buttons are gone from the kiosk entirely. Its `micOn` / `camOn` /
`sharing` signals now follow LiveKit's own events about its tracks rather than any local click,
because the teller is the only one who can change them.

**The teller's controls are two groups, KIOSK and YOU**, because they answer different questions —
what is the customer's kiosk doing, and what am I sending them. Seven controls: mute/unmute the
customer, their camera on/off, view/stop their screen; mute me, hide my video, share my screen, end
session.

**Two transports, chosen per command rather than uniformly:**

| Command | How | Why that way |
|---|---|---|
| Mute / unmute customer, their camera | **API → LiveKit server** | Authoritative, and keeps working when the kiosk page is wedged — which is when a teller most needs it |
| Kiosk screen share start/stop | **Browser-to-browser RPC** | A server cannot create a track; only the kiosk's own page can start a capture |
| "I'm finished" | **Browser-to-browser RPC** | Same channel, the other way |

**This does not need [D-011](#d-011)'s unwrapped `Twirp.PerformRpc`.** That entry is about the
*server* issuing RPC. Teller and kiosk are both in the room, and `livekit-client` lets participants
call each other directly — `registerRpcMethod` / `performRpc`. The blocker recorded there does not
apply to this, and the kiosk command channel is no longer waiting on it.

**Verified before promising it: a teller cannot start a screen capture on someone else's browser.**
Measured, not assumed:

| Kiosk browser | `getDisplayMedia()` with no gesture |
|---|---|
| Plain Chromium | **Opened a picker and waited for a human** — the one thing a kiosk customer cannot do |
| With `--auto-select-desktop-capture-source` | **`{"ok":true,"label":"screen:0:0"}`** — no gesture, no picker |

So the feature works, and the kiosk browser must be launched with that flag. That is exactly what
the WPF/WebView2 shell is for ([D-007](#d-007)). A kiosk started without it gets a clear toast on
the teller's side rather than a silent nothing.

**Still needs the shell: choosing *which* screen.** The flag is applied at launch, and a web page
cannot enumerate display sources. One screen works today; a picker for the teller is shell work.

**Verified end to end** — 18 checks across two runs, no console errors:

| | |
|---|---|
| Kiosk | exactly one button, and it is the exit request |
| Teller | two groups, seven controls |
| Mute customer | RMS **0.298 → 0**, button flips to Unmute, **0.306** back |
| Their camera off | placeholder shown, video back on |
| Their screen | arrived at **960x540** with nothing clicked on the kiosk; stopped again |
| "I'm finished" | kiosk confirms, teller gets the alert, **and the session keeps running** |

**Found while testing: a muted video track stays subscribed.** The teller's tile went on rendering
the last frame instead of showing "camera off". Camera off has to look like camera off, so the tile
now keys off the publication's mute state, not merely whether a track exists.

**The customer is not shown when their screen is being viewed**, by request, behind
`KIOSK_CONFIG.showScreenShareIndicator`. Worth repeating here because a reviewer will ask: telling
someone their screen is being watched is usually a compliance requirement in a bank rather than a
courtesy. It is one flag so that stays a one-line change.

---

### D-029 {#d-029}
**2026-09-23 · The data channel is not pub/sub — what that forces the design to be**

Asked directly whether LiveKit's data channels are publish/subscribe. They are not, and the answer
shapes everything built on them. Measured against the pinned SDK rather than assumed:

| | What it actually is |
|---|---|
| `publishData` | A **send**. To the room, or to named `destinationIdentities`. |
| `topic` | A **label carried in the packet**. Every participant receives every packet and filters it themselves — there is no subscription and no server-side routing. |
| Retention / replay | **None.** No queue, no buffer. Anything sent while you were away is gone. |
| RPC | Request/response, point to point. Not pub/sub at all. |

**So state cannot be listened for. It has to be asked for.** The kiosk owns its state and answers
`state.get`; a console that just joined, or just came back, asks. A broadcast on
`TOPICS.kioskState` is a *hint that something changed*, never the source of a fact.

**Reconnection, measured with `simulateScenario`:**

| Scenario | Result |
|---|---|
| `signal-reconnect` (a network blip) | Recovers by itself. **RPC handlers survive** — the handler map lives on the Room and nothing clears it. The data channel stays up, so nothing was even lost. |
| `server-leave` (evicted, node restarted) | **`Disconnected`, and it stays there.** The SDK does not come back. RPC times out, data stops. |

That second row is the one that mattered. Before this, `RoomEvent.Disconnected` called `reset()` on
the kiosk — which dropped a customer who was mid-transaction back to "Talk to a teller" while their
session was still open and a teller was still sitting in the room waiting for them.

**Built:**

- **`vtm-shared/`** — one copy of the contract and the channel, mapped into both apps through
  tsconfig `paths`. Two copies of a contract is not a contract.
- **`vtm-commands.ts`** — every method name, its request and response types, and its D-010 class,
  in one place. A name that exists on one side only is now a compile error rather than a button
  that quietly does nothing in a branch.
- **`CommandChannel`** — typed `handle`/`call`, re-registration on attach, the SDK's eleven RPC
  error codes translated into things a teller can act on, payload size checked before sending
  rather than silently truncated, and **the peer identity read from the room**.
- **`POST /api/sessions/{roomName}/rejoin`** — a fresh token for a session the caller is already
  part of. It creates nothing: a 404 means the session really has ended, which is the one case
  where returning to idle is right. A caller may only rejoin their own session.
- **Rejoin with backoff on both sides**, and a visible "Reconnecting…" rather than a frozen frame.

**Two things found by measuring that reading would not have caught:**

**The API prefixes participant identities.** Ask for `probe-a`, join as `teller-probe-a`. The first
probe failed with `1501 Connection timeout` — which looks exactly like a network fault — because it
addressed RPC by the name it had asked for. The channel now reads the peer out of the room, and
never assumes.

**`SignalReconnecting` is a different event from `Reconnecting`.** A network blip raises the first;
only a full reconnect raises the second. Listening for `Reconnecting` alone meant a customer watched
a frozen picture for 30 seconds with nothing said. Both are wired now.

**Verified end to end:** in a call, network cut, still in the call afterwards — never fell back to
the idle screen — and the teller could still mute the customer through the channel once it was back.

**Not solved: it still takes ~15s to tell the customer.** `ConnectionQuality` does not help, because
quality reports arrive over the signal connection and a cut cable produces none — measured. Lowering
`peerConnectionTimeout` does not help either: that governs how long to wait for a connection to come
*up*, not how long before a dead one is noticed. Tracked as **K-14**; the fix is a local watchdog on
the remote video's `currentTime`, which needs no server at all.

---

### D-030 {#d-030}
**2026-09-23 · K-14 closed: telling the customer in one second instead of fifteen**

[K-14](#5-known-issues) was the last thing left open from [D-029](#d-029): a cut network took about
fifteen seconds to reach the screen, so a customer mid-transaction watched a frozen teller with no
explanation. Two approaches had already been measured and rejected — `ConnectionQuality` reports
arrive over the signal connection, so a cut produces none, and `peerConnectionTimeout` governs how
long to wait for a connection to come *up*, not how long before a dead one is noticed.

**The answer was not to ask the SDK at all.** `LinkWatchdog` in `vtm-shared/` uses two local
signals, neither of which covers the other:

| Signal | Catches | Measured |
|---|---|---|
| `navigator.onLine` | This machine losing its network — by far the commonest kiosk failure | fires at **2ms** |
| Media standing still | The far end going away, or the path breaking while this machine looks healthy | 4s of no movement |

**Fifteen seconds became one.**

| | Before | After |
|---|---|---|
| Customer is told | t+15s | **t+1s** |
| What they saw until then | a frozen picture | "Reconnecting…" |

**The trap it had to avoid.** A camera the teller deliberately turned off also stops `currentTime`,
and calling that a dead link would put "Reconnecting…" over a working call. The probe returns
`undefined` when there is nothing legitimately to watch, which suspends the media half without
touching the network half. Verified: twenty seconds of a healthy call raised nothing, and turning
the teller's camera off and on again raised nothing.

The rejoin behaviour from D-029 is unchanged and still passes — the kiosk comes back into the same
call, never falls to the idle screen, and the teller can still drive it afterwards.

---

### D-031 {#d-031}
**2026-09-24 · The kiosk shell: a small teller window beside the bank's own application**

Built on `feature/wpf-kiosk-shell`. Design and reasoning are in [docs/07](07-kiosk-shell.md) and
`vtm-kiosk-shell/README.md`; this records what was decided and what testing turned up.

**The shape changed once, on request.** The first shell was a full-screen WebView hosting the kiosk
page. The real kiosk screen is small and already runs the bank's WPF application, so the teller now
appears in a **small window beside that application**, and every control — talk to a teller, I'm
finished, try again — stays in the host's own UI. The page detects `window.chrome.webview`, drops
its own chrome, and becomes a video surface. Three files are meant to be copied into the real
application: `TellerCall.cs`, `TellerCallWindow.cs`, `ShellConfig.cs`.

**Decisions:**

- **The page is bundled into the shell**, served by `SetVirtualHostNameToFolderMapping` under
  `https://kiosk.vtm`, so a kiosk needs no web server for its page. `file://` was not an option: its
  opaque origin sends `Origin: null` to the API, which no CORS setting can allow.
- **Settings are resolved at runtime**, injected by the shell before the page runs, so one Angular
  build serves every kiosk. Proved by giving one bundle `kioskId: K-02` — the teller saw
  *K-02 / Kandy City Lobby 2* while the bundle's default was still K-01.
- **Host and page talk over WebView2's own channel**: `start`, `end`, `retry`, `enableAudio` in; one
  state message per change out.
- **`dotnet build` stages the last Angular build into `bin/webroot`; `dotnet publish` runs the
  Angular build itself.**

**Found by testing, each invisible from reading:**

| | |
|---|---|
| `Policies(Kiosk, Staff)` means **both**, so rejoin refused every kiosk with 403 | new `SessionParticipant` policy |
| A session ended before acceptance is `Abandoned`, not `Ended`; rejoin let a kiosk into a dead session | rejoinable is an allow-list |
| Angular copies `public/` into `dist/`, so `kiosk.secret` was being bundled into every install | excluded from the bundle |
| `PublishDir` lacks a trailing separator sometimes; one copy landed in `publishwebroot/` with a live secret | `EnsureTrailingSlash` |
| WPF ignores `Opacity` without `AllowsTransparency`, so the "hidden" call window showed | parked off-screen instead |
| A stale bundle in `bin/` ran the old standalone page and ignored every host command | removed; `check.ps1` now detects it |
| **The queue kept sessions whose room was gone, and no endpoint could clear them** — both delete and status 404 once LiveKit forgets the room | `StaleSessionReaper`; first sweep retired sessions up to 51 hours old |

**The green "video" the teller saw was a test pattern, not a camera fault.** `fakeMedia: true`
replaces the camera with Chrome's generated pattern; it was on for automated runs and left on. The
shell now labels the call window *TEST VIDEO* and the host's title bar when it is on, and
`check.ps1 -Full` puts the real camera back when it finishes. With it off the kiosk sees
*HP True Vision HD Camera*.

**`check.ps1`** builds and checks everything in order — tools, services, whether both sides use the
same LiveKit, build, staged page, enrolment — and names the failing step.

**Still open:** the scripted end-to-end call through the shell (`check.ps1 -Full`) does not complete
on this machine — teller joins, kiosk stays waiting, and the API reports zero participants. The
manual test from a phone did carry video both ways, so the call path works; the scripted failure is
not yet explained. Free memory was under 1 GB during every failing run.

---

### D-032 {#d-032}
**2026-09-24 · The customer may stop a screen share; the teller is told. And the kiosk no longer misses an early teller.**

**The "is sharing your screen" bar is not hidden.** A Stop sharing button a customer can press
looked like a problem for a kiosk. The bar was found (a `Chrome_WidgetWin_1` titled
*"kiosk.vtm is sharing your screen."*) and hiding it from the host was attempted; the permission
system refused it as security-weakening. It is right to: the bar is the only thing telling the
customer their screen is being watched, and [D-028](#d-028) already recorded that in a bank this is
usually a compliance requirement. WebView2's `ScreenCaptureStarting` offers no way to suppress it
either — only to cancel the capture outright.

So stopping stays the customer's right, and the teller is told. When the browser ends the capture
the SDK unpublishes the share (`LocalParticipant.handleTrackEnded`); the kiosk sees
`LocalTrackUnpublished` and sends **`screenshare.ended`**, unless the teller's own stop is in
progress. The teller gets a toast and a note beside the button until they ask again.

**Found while testing it — a real race, not a test artefact.** The kiosk switched to the call only
on `ParticipantConnected`, which fires only for participants who arrive *after* you. The ring goes
out when the session is created, before the kiosk has finished connecting, so a quick teller is
often in the room first — and then the kiosk sat on *"Waiting for a teller…"* for ever while the
teller sat in the call. Isolated by one variable: both sides joined the same room with the same SID
every time, and the kiosk stuck only when it was the slower one (a headed browser, or WebView2).
A slow branch network would do the same. The kiosk now checks `remoteParticipants` after connecting.
The teller side already did this; the kiosk never had.

This explains the scripted end-to-end failure left open in [D-031](#d-031) — the kiosk waiting while
the teller was in the call. The *zero participants* reading from the API at the time is not
explained by it and stays unexplained.

**Verified**, headed kiosk with the capture flag, 9 checks: customer stop leaves the teller's view,
the teller is told, the button offers to ask again, the call keeps running; asking again clears the
note; the teller's own stop raises no customer notice. The race: two headed runs, kiosk in the call
both times, where before it failed every time.

`dotnet build` now clears `bin/webroot` before staging, so old hashed bundles do not pile up beside
the current one.

---

### D-033 {#d-033}
**2026-09-24 · An in-app share indicator; the browser's own bar stays**

The browser's *"kiosk.vtm is sharing your screen"* bar is a separate, draggable window, easy to
knock by accident on a kiosk. The request was to move it into the host application beside
*I'm finished*. Replacing it meant hiding the browser's bar; that was refused twice by the safety
systems — once when attempted at runtime ([D-032](#d-032)), once when writing the code that would do
it — even with an in-app indicator guaranteed in its place. **The bar is not hidden, and this
project does not ship code that hides it.**

What was built instead, all of which works with the bar left alone:

- The page reports `sharing` to the host with every state change.
- The demo host shows *"Your screen is being shared with the teller"* with a **Stop sharing** button
  beside the other controls whenever `sharing` is true.
- `TellerCall.StopSharingAsync()` sends `stopShare`; the page stops the share the same way the
  browser's button does, so the teller gets `screenshare.ended` and the note to ask again.

**Verified** with the page in hosted mode: host told on connect, told `sharing: true` when the
teller started viewing, told `sharing: false` after the host's Stop sharing, the kiosk screen left
the teller's view, the teller was told, and the call kept running.

---

## 7. Work log

Newest last. One line per piece of work. **Append only.**

| Date | Entry |
|---|---|
| 2026-09-18 | Reviewed `Livekit.Server.Sdk.Dotnet`, `Livekit.Rtc.Dotnet`, `livekit-client` against local source |
| 2026-09-18 | Created `docs/` — index + three library references (~1,900 lines) |
| 2026-09-18 | `PublishAot` → `false`; `PublishReadyToRun` added to publish profile |
| 2026-09-18 | Moved `AppJsonSerializerContext` from `ConfigureHttpJsonOptions` to `UseFastEndpoints` |
| 2026-09-18 | Verified: build clean, R2R publish clean, `POST /api/token` returns 200, decoded JWT has all 17 `video` claim properties, validation path returns the custom 400 envelope |
| 2026-09-18 | Documented decisions D-001 … D-012; created this file |
| 2026-09-18 | Created `docs/04-api-surface.md` (capability inventory, service layer, endpoint plan, build order) |
| 2026-09-18 | Restructured `docs/README.md` as a category/topic index; added root `CLAUDE.md` |
| 2026-09-18 | `git init` at workspace root, `.gitignore` for monorepo, initial commit `619265e` (28 files) |
| 2026-09-18 | Replaced `FileLoggerProvider` with Serilog (console + rolling file, config-driven); verified in Production mode |
| 2026-09-18 | Step 1 foundation: `ITokenService`/`IRoomService` split, `LiveKitOptions` validation, role→grants switch, exception handler, `/health`; fixed K-1, K-2, K-3, K-4, K-8 |
| 2026-09-18 | Manual test suite in `LivekitServerAPI.http`; validator `Cascade(CascadeMode.Stop)` |
| 2026-09-18 | Step 2 session lifecycle: create / list / get / status / end, verified end to end against LiveKit |
| 2026-09-18 | Settled the service boundary (D-017) and wrote `docs/05-data-model.md` — tables and the full operation inventory |
| 2026-09-18 | Participant control endpoints; found `MoveParticipant`/`ForwardParticipant` unimplemented on OSS and reworked transfer + monitor around it; fixed error-body casing |
| 2026-09-18 | Full endpoint sweep (40 cases, live participants): K-9 resolved by the service restart; mute now rejects an unknown `trackSid` instead of reporting a false success |
| 2026-09-21 | Persistence: EF Core + SQL Server, 10 tables, `VtmSessions` database; kiosk registry; sessions and their timeline now survive the room |
| 2026-09-21 | Auth Phase 1: JWT resource server, Local/Oidc modes, policies on every endpoint, kiosk enrolment, bootstrap admin. K-7 closed |
| 2026-09-21 | Queue + SignalR ring, `/api/me`, demo seed; verified with a real hub client. Demo scope agreed: web kiosk, no WPF yet |
| 2026-09-21 | Kiosk page (`vtm-kiosk`): idle → waiting → call → end, verified in a real browser with a teller joining |
| 2026-09-21 | Teller console (`vtm-teller`) on MUK UI Kit; full scenario verified in two browsers, 19 steps. Ring payload was missing the kiosk name — fixed |
| 2026-09-21 | Audio never played: `attach()` with no element creates an orphan. Fixed with a real `<audio>` element; verified by measuring sound |
| 2026-09-22 | A kiosk secret reached a commit. Rotated and revoked; the secret now lives in a gitignored file, not in tracked source |
| 2026-09-22 | Repo pushed to public GitHub. Credentials moved out of tracked config into a gitignored file plus a committed template; CORS was never allowing the kiosk |
| 2026-09-22 | Kiosk reduced to one button; the teller drives everything. Screen share proved to work unattended with the kiosk-mode flag ([D-028](#d-028)) |
| 2026-09-23 | Shared command channel, typed contract, state resync and rejoin. The data channel is not pub/sub, and `server-leave` does not recover by itself ([D-029](#d-029)) |
| 2026-09-23 | K-14 closed: a local watchdog tells the customer in ~1s instead of ~15s ([D-030](#d-030)) |
| 2026-09-24 | WPF kiosk shell: small teller window beside the host app, bundled page, runtime config, stale-session reaper ([D-031](#d-031)) |
| 2026-09-24 | Teller told when the customer stops a screen share; kiosk no longer misses a teller who joined first ([D-032](#d-032)) |
| 2026-09-24 | In-app share indicator and Stop sharing in the host; the browser's own bar is left alone ([D-033](#d-033)) |

---

## Template for new entries

```markdown
### D-0NN
**YYYY-MM-DD · One-line summary**

What changed.

**Why:** the reasoning, including what was considered and rejected.

**Verified:** how we know it works (if applicable).
```
