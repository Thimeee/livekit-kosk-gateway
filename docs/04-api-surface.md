# API surface — what the SDK offers, and how we expose it

What `Livekit.Server.Sdk.Dotnet` can do in full, and the endpoint plan that maps onto it.

Status of each item is tracked in [PROJECT.md §3](PROJECT.md#3-component-status), not here — this
document describes the *design*, not the progress.

---

## 1. Capability inventory

Everything the library can do, grouped by what infrastructure it requires.

### 1.1 Tokens — no extra infrastructure

| Capability | Type |
|---|---|
| Generate an access token (grants, TTL, identity, name, metadata, attributes, kind, roomPreset, roomConfig) | `AccessToken` |
| Verify and decode a token | `TokenVerifier` |

### 1.2 Room & participant control — no extra infrastructure

`RoomServiceClient`, 13 wrapped methods plus one that is not wrapped.

| # | Method | What it does |
|---|---|---|
| 1 | `CreateRoom` | Create a room with settings — `MaxParticipants`, timeouts, metadata, egress and agent config |
| 2 | `ListRooms` | List active rooms, optionally filtered by name |
| 3 | `DeleteRoom` | Destroy the room and disconnect everyone |
| 4 | `ListParticipants` | Everyone in a room, with their published tracks |
| 5 | `GetParticipant` | One participant's full info |
| 6 | `RemoveParticipant` | Kick someone. Set `RevokeTokenTs` to stop them rejoining |
| 7 | `MutePublishedTrack` | Server-side mute/unmute of a specific track |
| 8 | `UpdateParticipant` | Change metadata, name or **permissions at runtime** without reissuing a token |
| 9 | `UpdateSubscriptions` | Control who subscribes to whose tracks |
| 10 | `SendData` | Server-originated data message, broadcast or targeted |
| 11 | `UpdateRoomMetadata` | Change room metadata; broadcasts to everyone |
| 12 | `ForwardParticipant` | ⚠️ **Cloud only** — returns "not implemented" on OSS. See [D-018](PROJECT.md#d-018) |
| 13 | `MoveParticipant` | ⚠️ **Cloud only** — returns "not implemented" on OSS. See [D-018](PROJECT.md#d-018) |
| 14 | `Twirp.PerformRpc` ⚠️ | **Call an RPC method on a participant and get a response.** Not wrapped — see [D-011](PROJECT.md#d-011) |

### 1.3 Webhooks — no extra service, one `livekit.yaml` block

`WebhookReceiver` verifies the JWT and the body SHA-256, then parses the event.

Events: `room_started`, `room_finished`, `participant_joined`, `participant_left`,
`track_published`, `track_unpublished`, `egress_started`, `egress_updated`, `egress_ended`,
`ingress_started`, `ingress_ended`

```yaml
webhook:
  api_key: devkey
  urls:
    - http://<api-host>:5065/api/webhooks/livekit
```

### 1.4 Egress — ⚠️ requires `livekit-egress` + Redis

`EgressServiceClient`, 9 methods plus one unwrapped.

`StartRoomCompositeEgress` · `StartWebEgress` · `StartParticipantEgress` ·
`StartTrackCompositeEgress` · `StartTrackEgress` · `UpdateLayout` · `UpdateStream` · `ListEgress` ·
`StopEgress` · `Twirp.StartEgress` ⚠️

Outputs: MP4 / WebM / OGG file, RTMP stream, HLS segments, raw WebSocket.

For VTM, `StartRoomCompositeEgress` is the relevant one — the whole session, composited.

### 1.5 Ingress — ⚠️ requires `livekit-ingress`

`CreateIngress` · `UpdateIngress` · `ListIngress` · `DeleteIngress`

RTMP, WHIP, or URL pull. Possible VTM use: inject a welcome video or a branch camera feed.

### 1.6 SIP — ⚠️ requires `livekit-sip`

`SipServiceClient`, 16 methods.

- **Trunks (10):** `CreateSIPInboundTrunk`, `CreateSIPOutboundTrunk`, `GetSIPInboundTrunk`,
  `GetSIPOutboundTrunk`, `ListSIPInboundTrunk`, `ListSIPOutboundTrunk`, `UpdateSIPInboundTrunk`,
  `UpdateSIPOutboundTrunk`, `DeleteSIPTrunk`, `ListSIPTrunk` *(deprecated)*
- **Dispatch rules (4):** `CreateSIPDispatchRule`, `ListSIPDispatchRule`, `UpdateSIPDispatchRule`,
  `DeleteSIPDispatchRule`
- **Participants (2):** `CreateSIPParticipant` (dial out into a room), `TransferSIPParticipant`

Possible VTM use: phone fallback when video fails.

### 1.7 Agents — ⚠️ requires an agent worker (Python or Node)

`CreateDispatch` · `DeleteDispatch` · `ListDispatch`

Possible VTM use: an AI assistant greeting the customer while they wait in the queue. Overlaps with
the parked `Livekit.Rtc.Dotnet` feature, [D-008](PROJECT.md#d-008).

---

## 2. What LiveKit does **not** give us

Worth stating plainly, because it is easy to assume otherwise:

- **Queue, ring-out and teller assignment.** LiveKit's involvement starts *after* a teller accepts.
  The waiting list, notifying available tellers, and assigning one are our API + database + a push
  channel (SignalR or SSE). The teller is not in a room yet, so LiveKit cannot carry that
  notification.
- **Remote desktop control.** Screen share is one-way video. Control is a command set we define
  ([D-009](PROJECT.md#d-009)).
- **Persistence.** Rooms are ephemeral. Session history, audit and transactions are our database.
- **Authentication.** LiveKit trusts any JWT signed with the shared secret. Every authorisation
  decision happens in our API before a token is minted.

---

## 3. Service layer

`ILiveKitService` was split in [D-015](PROJECT.md#d-015); the remaining rows are still to come.

| Interface | Wraps | Lifetime |
|---|---|---|
| `ITokenService` ✅ | `AccessToken`, `TokenVerifier` | **Singleton** — stateless, safe |
| `IRoomService` ✅ | `RoomServiceClient` | **Scoped** — see warning |
| `IKioskControlService` | `Twirp.PerformRpc`, `SendData` | **Scoped** |
| `IRecordingService` | `EgressServiceClient` | **Scoped**, later |
| `IWebhookService` | `WebhookReceiver` → DB | Singleton |

> ⚠️ **`RoomServiceClient` must not be a singleton.** Every method mutates
> `httpClient.DefaultRequestHeaders.Authorization` before sending, so concurrent requests can send
> each other's tokens — one branch's teller acting with another branch's grants. Use
> `IHttpClientFactory` with a scoped client, or serialise with a `SemaphoreSlim`. Full explanation
> in [01 §10](01-livekit-server-sdk-dotnet.md#10-concurrency). Resolved in [D-015](PROJECT.md#d-015).

---

## 4. Endpoint plan

### 4.1 Queue & ring — our logic, not LiveKit's

```
POST   /api/queue/enqueue          customer arrives at a kiosk → create room (status: waiting)
GET    /api/queue                  waiting list for available tellers
POST   /api/queue/{room}/accept    teller accepts → issue teller token, mark room active
```

The ring-out to tellers is a push channel (SignalR/SSE) from our API. This is the front of the whole
scenario and the part LiveKit contributes nothing to.

### 4.2 Session lifecycle

```
POST   /api/sessions                    → CreateRoom + kiosk token in one round trip
GET    /api/sessions                    → ListRooms          (supervisor dashboard)
GET    /api/sessions/{room}             → ListParticipants
DELETE /api/sessions/{room}             → DeleteRoom
PATCH  /api/sessions/{room}/metadata    → UpdateRoomMetadata
```

Create rooms with `MaxParticipants = 3` — kiosk, teller, and one hidden supervisor. A cheap guard
that means nobody can gatecrash a session even if a token leaks. See [D-016](PROJECT.md#d-016) for
why not 2.

### 4.3 Tokens

```
POST   /api/tokens/kiosk        RoomJoin, CanPublishSources = [camera, microphone]
POST   /api/tokens/teller       RoomJoin + RoomAdmin, scoped to one room
POST   /api/tokens/supervisor   RoomJoin + Hidden + CanSubscribe, CanPublish = false
```

Room creation and recording stay API-side rather than being granted to a browser — see K-3.

### 4.4 Participant control

```
POST   /api/sessions/{room}/participants/{id}/mute   → MutePublishedTrack
DELETE /api/sessions/{room}/participants/{id}        → RemoveParticipant
PATCH  /api/sessions/{room}/participants/{id}        → UpdateParticipant
POST   /api/sessions/{room}/notify                   → SendData
```

Omit `trackSid` on mute to mute every track the participant publishes — which is what a teller
means by "mute the customer".

### 4.5 Kiosk control — Class 2 commands only

Class 1 (UI/form) commands go peer-to-peer and never touch these endpoints. See
[D-010](PROJECT.md#d-010).

```
POST   /api/sessions/{room}/control/card-read    → Twirp.PerformRpc  (authorize → execute → log result)
POST   /api/sessions/{room}/control/print        → Twirp.PerformRpc
POST   /api/sessions/{room}/control/signature    → Twirp.PerformRpc
POST   /api/sessions/{room}/notify               → SendData          (fire-and-forget)
```

`PerformRpc` returns the kiosk's actual result, so the audit record says *succeeded* rather than
*attempted*.

### 4.6 Submit & audit

```
POST   /api/sessions/{room}/submit    transaction payload + the kiosk's batched command log
```

### 4.7 Supervisor

```
POST   /api/sessions/{room}/monitor    → hidden supervisor token for the SAME room
POST   /api/sessions/{room}/transfer   → swap the teller inside the room, customer stays
```

### 4.8 Webhooks

```
POST   /api/webhooks/livekit    → WebhookReceiver → session state, durations, join/leave times
```

Worth building before it is needed — reconstructing call history afterwards is painful, and it is
the only source of session timings we do not have to trust a client for.

### 4.9 Recording — after `livekit-egress` is deployed

```
POST   /api/sessions/{room}/recording    → StartRoomCompositeEgress
DELETE /api/sessions/{room}/recording    → StopEgress
GET    /api/recordings                   → ListEgress
```

---

## 5. Suggested build order

1. **Persistence** — sessions, queue, audit. Everything else needs it (K-6).
2. **Session lifecycle** — `CreateRoom` / `DeleteRoom`, and fix K-1, K-2, K-3 while touching tokens.
3. **Queue & ring** + the push channel. This is the front of the scenario.
4. **Webhooks** — cheap now, expensive to retrofit.
5. **Kiosk command channel** — `Twirp.PerformRpc`, once the kiosk app exists to answer.
6. **Participant control & supervisor** endpoints.
7. **Recording**, after the egress service is deployed.

Settle the `RoomServiceClient` lifetime question (K-4) at step 2 — it is much cheaper to get right
once than to retrofit across every endpoint.
