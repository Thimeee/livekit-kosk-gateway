# VTM — Documentation index

Documentation for the VTM (Video Teller Machine) system. Library references are written against the
exact source checked out in this workspace, not against public docs.

> ### 👉 Start here: **[PROJECT.md](PROJECT.md)**
> Current project state, component status, known issues, and the dated decision log.
> **If you are picking this project up — human or AI agent — read that first.**
> It is the source of truth for *where we are* and *why things are the way they are*.

---

## By category

### Project

| Document | What |
|---|---|
| [PROJECT.md](PROJECT.md) | **Living state + append-only decision log.** Updated with every piece of work. |
| [04-api-surface.md](04-api-surface.md) | Full SDK capability inventory, service layer design, endpoint plan, build order |
| [05-data-model.md](05-data-model.md) | Service boundary, database tables, and everything this API does |
| [06-kiosk-command-set.md](06-kiosk-command-set.md) | **The contract between teller and kiosk** — what the teller can make the kiosk do, over which transport, and how to add a command |

### Library references

| # | Document | Library | Used by |
|---|---|---|---|
| 01 | [Livekit.Server.Sdk.Dotnet](01-livekit-server-sdk-dotnet.md) | `Livekit.Server.Sdk.Dotnet` v1.2.3 | `LivekitServerAPI` (control plane) |
| 02 | [Livekit.Rtc.Dotnet](02-livekit-rtc-dotnet.md) | `Livekit.Rtc.Dotnet` v0.1.4 | *Parked* — later server-side participant feature |
| 03 | [livekit-client (JS)](03-livekit-client-js.md) | `livekit-client` v2.22.3 | Teller (Angular) **and** Kiosk (WPF + WebView2) |

### By topic

| Looking for | Go to |
|---|---|
| Access tokens, grants, role mapping | [01 §2–3](01-livekit-server-sdk-dotnet.md#2-accesstoken) |
| Room / participant management | [01 §4](01-livekit-server-sdk-dotnet.md#4-roomserviceclient) |
| Recording (egress) | [01 §5](01-livekit-server-sdk-dotnet.md#5-egressserviceclient) |
| Webhooks | [01 §8](01-livekit-server-sdk-dotnet.md#8-webhookreceiver) |
| Thread-safety of service clients | [01 §10](01-livekit-server-sdk-dotnet.md#10-concurrency) |
| Native AOT constraints | [01 §11](01-livekit-server-sdk-dotnet.md#11-native-aot) |
| Driving the kiosk from the teller | [06](06-kiosk-command-set.md) |
| Why a command uses the server or RPC | [06 §2](06-kiosk-command-set.md#2-which-transport-and-why) |
| Rendering remote audio (do **not** use bare `attach()`) | [03 §4](03-livekit-client-js.md#4-rendering-remote-media) |
| Why the kiosk uses WebView2 | [02 §2](02-livekit-rtc-dotnet.md#2-the-wpf-decision) |
| Angular service pattern, NgZone, cleanup | [03 §6](03-livekit-client-js.md#6-an-angular-service) |
| Autoplay / "can't hear the customer" | [03 §7](03-livekit-client-js.md#7-autoplay) |
| Data messages and RPC between clients | [03 §8–9](03-livekit-client-js.md#8-data-messages) |
| Endpoint plan and build order | [04](04-api-surface.md#4-endpoint-plan) |
| What this API owns vs the bank's core | [05 §2](05-data-model.md#2-the-dividing-line) |
| Database tables | [05 §3](05-data-model.md#3-tables) |

---

## Maintaining these docs

- **[PROJECT.md](PROJECT.md)** is updated with every piece of work. Sections 1–5 are rewritten;
  sections 6–7 are **append-only** and must never be edited.
- Library docs (01–03) change only when the library version changes or we find something new in
  the source.
- [04-api-surface.md](04-api-surface.md) describes *design*. Progress lives in PROJECT.md §3.

Source checkouts:

- `livekit-server-sdk-dotnet-main/LivekitApi/` → `Livekit.Server.Sdk.Dotnet`
- `livekit-server-sdk-dotnet-main/LivekitRtc/` → `Livekit.Rtc.Dotnet`
- `client-sdk-js-main/` → `livekit-client`

---

## 1. The system

```
        CONTROL PLANE (HTTP/REST)              MEDIA PLANE (WebRTC / WSS)
        ─────────────────────────              ──────────────────────────

   ┌──────────────┐                                   ┌──────────────┐
   │  Kiosk (WPF) │──── POST /api/token ─────────────▶│              │
   │   customer   │                                   │              │
   └──────┬───────┘                                   │              │
          │                                           │   LiveKit    │
          │◀───────── JWT ────────────────────────────│   Server     │
          │                                           │              │
          └───────── WebRTC: audio/video/data ───────▶│ livekit-     │
                                                      │ server.exe   │
   ┌──────────────┐                                   │              │
   │Teller(Angular)│──── POST /api/token ─────────────▶│  :7880 http  │
   │    agent     │                                   │  :7881 tcp   │
   └──────┬───────┘◀──────── JWT ─────────────────────│  :50000-     │
          │                                           │   60000 udp  │
          └───────── WebRTC: audio/video/data ───────▶│              │
                                                      └──────▲───────┘
   ┌──────────────────────────┐                              │
   │   LivekitServerAPI       │───── Twirp/protobuf ─────────┘
   │  .NET 10 + FastEndpoints │   (create room, list/remove
   │  PublishAot = true       │    participants, mute, egress)
   └──────────────────────────┘
```

### Two planes, never mix them up

**Control plane** — your `LivekitServerAPI` talking *to* the LiveKit server over HTTP. It mints
JWTs and issues management commands. It never carries audio or video. This is
`Livekit.Server.Sdk.Dotnet` (doc 01). The API key and secret live **only here**.

**Media plane** — the kiosk and the teller talking *to* the LiveKit server over WebRTC. Both are
clients. They authenticate with a JWT they received from your API; they never see the secret.
The teller uses `livekit-client` (doc 03). The kiosk uses `Livekit.Rtc.Dotnet` or an embedded
browser (doc 02 explains the trade-off).

### Token flow

1. Kiosk/teller app calls `POST /api/token` on `LivekitServerAPI` with role + room + identity.
2. The API authenticates the caller (**your own auth — not LiveKit's**), then builds an
   `AccessToken` with the right `VideoGrants` and returns the JWT.
3. The app calls `room.connect(wsUrl, jwt)` and the LiveKit server validates the JWT against the
   shared secret from `livekit.yaml`.

The LiveKit server trusts any JWT signed with the shared secret. **All authorisation decisions are
made at step 2, inside your API.** A kiosk must never be handed a token with `RoomAdmin` or
`RoomCreate`.

---

## 2. Which library goes where

| Component | Library | Notes |
|---|---|---|
| `LivekitServerAPI` | `Livekit.Server.Sdk.Dotnet` | Already referenced. Only token generation is wired up so far. |
| Teller — Angular | `livekit-client` (npm) | Straightforward. Browser handles camera/mic/rendering. |
| Kiosk — WPF | **WebView2 + `livekit-client`** | Decided. See [doc 02 §2](02-livekit-rtc-dotnet.md#2-the-wpf-decision). |
| *(later)* server-side participant | `Livekit.Rtc.Dotnet` | Parked. Auto-guide / transcription / recording bot. |

**Why the kiosk is not native .NET.** There is no official LiveKit .NET client SDK — LiveKit's own
SDK list marks .NET as *(community)*, and that community SDK is `Livekit.Rtc.Dotnet` in this
workspace. It is a real client, but it was built for *server-side* participants: it has no camera
capture, no microphone capture, no speaker playback, no video rendering and **no echo cancellation
exposed**. You feed it raw frames and it hands you raw frames back. A kiosk with a speaker and a
mic in one enclosure needs AEC, and building that yourself is not a reasonable trade for a
two-party video call. WebView2 hosting `livekit-client` gets all of it from Chromium, and lets the
kiosk and the teller share one client implementation.

Three WebView2 specifics that matter for a kiosk, covered in doc 02 §2: `getUserMedia` needs a
secure context (use `SetVirtualHostNameToFolderMapping`, not `file://`), `PermissionRequested` must
be auto-granted since nobody is there to click Allow, and a **Fixed Version** runtime avoids
Evergreen auto-updates breaking a deployed fleet.

---

## 3. Current environment

**LiveKit server** — `LivekitServer/livekit.yaml`:

```yaml
port: 7880
rtc:
  tcp_port: 7881
  port_range_start: 50000
  port_range_end: 60000
keys:
  devkey: "this_is_a_very_long_secret_key_for_livekit_development_123456"
```

- Control-plane URL (for the .NET SDK): `http://<host>:7880`
- Media-plane URL (for clients): `ws://<host>:7880`, or `wss://...` behind TLS

The UDP range 50000–60000 must be open end-to-end or media will fail while signalling succeeds —
a classic "connected but black screen" symptom.

**API** — `LivekitServerAPI/.../appsettings.json` currently points `LiveKit:ServerUrl` at
`https://monapisam.nipunmcs.biz` while the local server runs on `:7880`. The API secret is also
stored in plain text in `appsettings.json`; move it to environment variables or user-secrets before
this leaves the dev machine.

---

## 4. Things that will bite you

These are documented in detail in the individual docs, collected here so they are not missed.

1. **Native AOT is off — deliberately.** The SDK builds the `video` claim by reflection
   (`Type.GetProperties()`), `WebhookReceiver.Receive()` parses protobuf JSON by reflection, and
   `WithRoomConfig()` uses `JsonFormatter`. All three are patterns the trimmer breaks, and the
   failure mode is a token that looks valid but carries an empty `video` claim. The API now
   publishes with `PublishAot=false` and ReadyToRun instead.
   → [doc 01 §11](01-livekit-server-sdk-dotnet.md#11-native-aot).

2. **Service clients are not thread-safe** — every method mutates
   `httpClient.DefaultRequestHeaders.Authorization` before sending. One shared instance across
   concurrent requests can leak one request's grants into another's call.
   → [doc 01 §8](01-livekit-server-sdk-dotnet.md#8-concurrency).

3. **`apiSecret` must be ≥ 32 bytes** or the constructor throws. The dev secret above is fine.

4. **FastEndpoints has its own JSON serializer options.** It does *not* read
   `ConfigureHttpJsonOptions`. The source-generated `AppJsonSerializerContext` must be registered
   on `c.Serializer.Options` inside `UseFastEndpoints(...)`, which is where it now lives.

5. **Autoplay** — browsers block audio until a user gesture. The Angular teller app must handle
   `RoomEvent.AudioPlaybackStatusChanged` and call `room.startAudio()`.
   → [doc 03 §7](03-livekit-client-js.md#7-autoplay).

6. **`Livekit.Rtc.Dotnet` ships native binaries** for 6 platforms. Parked for now — the kiosk uses
   WebView2 instead. See [doc 02 §2](02-livekit-rtc-dotnet.md#2-the-wpf-decision).
