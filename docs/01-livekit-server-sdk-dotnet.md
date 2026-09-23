# Livekit.Server.Sdk.Dotnet

**Version** 1.2.3 · **Target** `netstandard2.0` · **Assembly** `LivekitApi.dll` ·
**Namespace** `Livekit.Server.Sdk.Dotnet`
**Source** `livekit-server-sdk-dotnet-main/LivekitApi/`

The server-side management SDK. This is the library the VTM control API is built on. It does two
things: **mint access tokens**, and **issue management commands to the LiveKit server**. It carries
no media.

```bash
dotnet add package Livekit.Server.Sdk.Dotnet --version 1.2.3
```

Dependencies it drags in: `Google.Protobuf`, `Newtonsoft.Json`,
`System.IdentityModel.Tokens.Jwt`.

---

## Contents

1. [How it works](#1-how-it-works)
2. [AccessToken](#2-accesstoken)
3. [VideoGrants](#3-videogrants)
4. [RoomServiceClient](#4-roomserviceclient)
5. [EgressServiceClient](#5-egressserviceclient)
6. [IngressServiceClient](#6-ingressserviceclient)
7. [SipServiceClient & AgentDispatchServiceClient](#7-sipserviceclient--agentdispatchserviceclient)
8. [WebhookReceiver](#8-webhookreceiver)
9. [Error handling](#9-error-handling)
10. [Concurrency](#10-concurrency)
11. [Native AOT](#11-native-aot)
12. [What VTM actually needs](#12-what-vtm-actually-needs)

---

## 1. How it works

Every service client derives from `BaseService`
(`livekit-server-sdk-dotnet-main/LivekitApi/BaseService.cs`):

```csharp
public BaseService(string host, string apiKey, string apiSecret, HttpClient client = null)
```

The constructor validates the credentials, creates (or adopts) an `HttpClient`, and sets
`BaseAddress = host`. Then **every single API method follows the identical three-step shape**:

```csharp
public async Task<ListRoomsResponse> ListRooms(ListRoomsRequest request)
{
    // 1. mint a short-lived JWT carrying only the grants this call needs
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", AuthHeader(new VideoGrants { RoomList = true }));

    // 2. POST the protobuf-encoded request to the Twirp route
    return await Twirp.ListRooms(httpClient, request);
}
```

`Twirp` (`proto/Twirp.cs`) is the transport. It serialises the request message to protobuf, POSTs it
with `Content-Type: application/protobuf` to `twirp/livekit.<Service>/<Method>`, and parses the
response. All 47 RPCs go through one generic helper.

> `Twirp` is declared in the **global namespace**, not inside `Livekit.Server.Sdk.Dotnet`. If you
> ever get a strange name collision on a type called `Twirp`, that is why.

Note the consequence of step 1: the SDK creates a **fresh 6-hour JWT on every call**, scoped to
exactly that operation. You do not manage server-side tokens yourself.

### Constructing a client

```csharp
var rooms = new RoomServiceClient(
    host:      "http://192.168.1.190:7880",   // HTTP, not ws://
    apiKey:    "devkey",
    apiSecret: "this_is_a_very_long_secret_key_for_livekit_development_123456");
```

`host` must be the **HTTP** URL of the LiveKit server. Using `ws://` here will fail.

If `apiKey`/`apiSecret` are null the SDK falls back to the `LIVEKIT_API_KEY` /
`LIVEKIT_API_SECRET` environment variables. The secret must be **at least 32 bytes (256 bits)** or
the constructor throws `ArgumentException`.

---

## 2. AccessToken

`livekit-server-sdk-dotnet-main/LivekitApi/AccessToken.cs`

Builds the JWT that a client presents to the LiveKit server. This is the part the VTM API already
uses.

```csharp
var token = new AccessToken(apiKey, apiSecret)
    .WithIdentity("kiosk-001")
    .WithName("Kiosk 001")
    .WithMetadata(metadataJson)
    .WithTtl(TimeSpan.FromMinutes(15))
    .WithGrants(new VideoGrants { RoomJoin = true, Room = "vtm-session-42" });

string jwt = token.ToJwt();
```

### Builder methods

| Method | Effect |
|---|---|
| `WithIdentity(string)` | **Required for join tokens.** Unique participant id. Becomes `sub` and `jti`. Two connections with the same identity in one room, and the first is kicked. |
| `WithName(string)` | Display name, surfaces as `Participant.name` on clients. |
| `WithMetadata(string)` | Arbitrary string (use JSON) visible to all participants. Good place for the VTM role. |
| `WithAttributes(Dictionary<string,string>)` | Key/value pairs, individually updatable at runtime. Better than metadata when you need to change one field. |
| `WithGrants(VideoGrants)` | The permission set. See section 3. |
| `WithSipGrants(SIPGrants)` | `Admin` / `Call` for SIP operations. |
| `WithTtl(TimeSpan)` | Validity window. **Default 6 hours**, which is too long for a kiosk. |
| `WithExpiration(DateTime)` | Absolute expiry; overrides `WithTtl`. |
| `WithNotBefore(DateTime)` | Token invalid before this instant. |
| `WithKind(ParticipantInfo.Types.Kind)` | `Standard`, `Egress`, `Ingress`, `Agent`, `Sip`. |
| `WithRoomPreset(string)` | Named server-side room config preset. |
| `WithRoomConfig(RoomConfiguration)` | Inline room config applied when this participant creates the room (agents, egress). |
| `ToJwt()` | Produces the signed HS256 string. |

### Validation inside `ToJwt()`

```csharp
if (video.RoomJoin && (string.IsNullOrEmpty(Claims.Identity) || string.IsNullOrEmpty(video.Room)))
    throw new ArgumentException("identity and room must be set when joining a room");
```

That is the only guard. Everything else is accepted, including nonsense grant combinations, so the
correctness of your role mapping is entirely on your API.

### TTL matters

The default 6 hours is a long window for a leaked kiosk token. A VTM session is minutes. Set
`WithTtl(TimeSpan.FromMinutes(15))` or so. The TTL controls how long the token can be used **to
join**; an established connection is not dropped when the token expires.

### TokenVerifier

```csharp
var verifier = new TokenVerifier(apiKey, apiSecret);
ClaimsModel claims = verifier.Verify(jwt);   // throws if invalid or expired
```

Validates issuer, signature and lifetime (1 minute default clock skew) and returns the decoded
claims. Useful if a kiosk hands a token back to your API and you want to know who it is without a
separate session store.

---

## 3. VideoGrants

`livekit-server-sdk-dotnet-main/LivekitApi/Grants.cs`

The permission set embedded in the token. **Note the defaults** — three of them are `true`, which
means an empty `new VideoGrants()` is already permissive about publishing.

| Property | Default | Meaning |
|---|---|---|
| `Room` | `""` | Room name. **Required** when `RoomJoin` or `RoomAdmin` is set. |
| `RoomJoin` | `false` | Can connect to `Room` as a participant. |
| `RoomCreate` | `false` | Can create and delete **any** room. Management-level. |
| `RoomList` | `false` | Can list rooms. Management-level. |
| `RoomAdmin` | `false` | Can administer `Room`: remove participants, mute tracks, update metadata. |
| `RoomRecord` | `false` | Can start egress/recording. |
| `CanPublish` | **`true`** | May publish media. |
| `CanSubscribe` | **`true`** | May subscribe to other participants' media. |
| `CanPublishData` | **`true`** | May send data messages. |
| `CanPublishSources` | `[]` | Whitelist of sources (`camera`, `microphone`, `screen_share`, `screen_share_audio`). **When non-empty it supersedes `CanPublish`** and only the listed sources are allowed. |
| `CanSubscribeMetrics` | `false` | May receive metrics. |
| `CanUpdateOwnMetadata` | `false` | May change own metadata/attributes from the client. |
| `Hidden` | `false` | Participant is invisible to others (monitoring or recording bots). |
| `Recorder` | `false` | Marks the room as being recorded. |
| `Agent` | `false` | Connecting as an Agent Framework worker. |
| `IngressAdmin` | `false` | Global ingress management. |
| `DestinationRoom` | `""` | Target room for `ForwardParticipant` / `MoveParticipant`. |

### VTM role mapping

The current `LiveKitService` gives the teller `RoomAdmin`, `RoomCreate`, `Recorder` **and**
`RoomRecord`. That is a lot of authority in a browser-held token. Consider:

```csharp
// Kiosk - customer. Join and talk, nothing else.
new VideoGrants {
    RoomJoin = true,
    Room = roomName,
    CanPublish = true,
    CanSubscribe = true,
    CanPublishData = true,
    CanPublishSources = { "camera", "microphone" },   // no screen share from a kiosk
}

// Teller - agent. Can moderate this one room, cannot create or list rooms.
new VideoGrants {
    RoomJoin = true,
    Room = roomName,
    RoomAdmin = true,          // mute/remove within this room only
    CanPublish = true,
    CanSubscribe = true,
    CanPublishData = true,
}
```

Room creation and recording are better done **server-side through your API** (an authenticated
`POST /api/rooms`, `POST /api/rooms/{name}/recording`) than by handing `RoomCreate` and
`RoomRecord` to the browser. The API already holds the secret; it can mint an admin token per
operation with no extra exposure.

`Hidden = true` is worth remembering: a supervisor who needs to monitor a session without appearing
in it gets `RoomJoin + Hidden + CanSubscribe`, with `CanPublish = false`.

---

## 4. RoomServiceClient

`livekit-server-sdk-dotnet-main/LivekitApi/RoomServiceClient.cs` — the main management surface.
13 methods. The "Grants" column is what the SDK requests internally; it tells you what authority
the call needs.

| Method | Request to Response | Grants used |
|---|---|---|
| `CreateRoom` | `CreateRoomRequest` to `Room` | `RoomCreate` |
| `ListRooms` | `ListRoomsRequest` to `ListRoomsResponse` | `RoomList` |
| `DeleteRoom` | `DeleteRoomRequest` to `DeleteRoomResponse` | `RoomCreate` |
| `ListParticipants` | `ListParticipantsRequest` to `ListParticipantsResponse` | `RoomAdmin` + `Room` |
| `GetParticipant` | `RoomParticipantIdentity` to `ParticipantInfo` | `RoomAdmin` + `Room` |
| `RemoveParticipant` | `RoomParticipantIdentity` to `RemoveParticipantResponse` | `RoomAdmin` + `Room` |
| `MutePublishedTrack` | `MuteRoomTrackRequest` to `MuteRoomTrackResponse` | `RoomAdmin` + `Room` |
| `UpdateParticipant` | `UpdateParticipantRequest` to `ParticipantInfo` | `RoomAdmin` + `Room` |
| `UpdateSubscriptions` | `UpdateSubscriptionsRequest` to `UpdateSubscriptionsResponse` | `RoomAdmin` + `Room` |
| `SendData` | `SendDataRequest` to `SendDataResponse` | `RoomAdmin` + `Room` |
| `UpdateRoomMetadata` | `UpdateRoomMetadataRequest` to `Room` | `RoomAdmin` + `Room` |
| `ForwardParticipant` | `ForwardParticipantRequest` to `ForwardParticipantResponse` | `RoomAdmin` + `Room` + `DestinationRoom` |
| `MoveParticipant` | `MoveParticipantRequest` to `MoveParticipantResponse` | `RoomAdmin` + `Room` + `DestinationRoom` |

### CreateRoom

Rooms are created implicitly when the first participant joins, so this is only needed to customise
settings up front.

```csharp
Room room = await rooms.CreateRoom(new CreateRoomRequest {
    Name             = "vtm-session-42",
    EmptyTimeout     = 300,      // seconds to keep an empty room alive
    DepartureTimeout = 20,       // seconds after the last participant leaves
    MaxParticipants  = 2,        // kiosk + teller
    Metadata         = sessionMetadataJson,
});
```

`CreateRoomRequest` fields: `Name`, `RoomPreset`, `EmptyTimeout`, `DepartureTimeout`,
`MaxParticipants`, `NodeId`, `Metadata`, `MinPlayoutDelay`, `MaxPlayoutDelay`, `SyncStreams`,
`ReplayEnabled`, plus `Egress` and `Agents` configuration.

`MaxParticipants = 2` is a cheap, effective guard for a VTM session: nobody can gatecrash even if a
token leaks.

Returned `Room`: `Sid`, `Name`, `EmptyTimeout`, `DepartureTimeout`, `MaxParticipants`,
`CreationTime`, `CreationTimeMs`, `TurnPassword`, `Metadata`, `NumParticipants`, `NumPublishers`,
`ActiveRecording`.

### ListRooms

```csharp
var all  = await rooms.ListRooms(new ListRoomsRequest());               // every active room
var some = await rooms.ListRooms(new ListRoomsRequest { Names = { "vtm-session-42" } });
```

`Names` is a repeated field, so use collection-initialiser syntax. It has no setter.

### DeleteRoom

```csharp
await rooms.DeleteRoom(new DeleteRoomRequest { Room = "vtm-session-42" });
```

Disconnects everyone and destroys the room. This is how the teller ends a VTM session cleanly.

### ListParticipants / GetParticipant

```csharp
var list = await rooms.ListParticipants(new ListParticipantsRequest { Room = roomName });
foreach (var p in list.Participants)
    Console.WriteLine($"{p.Identity} {p.State} publisher={p.IsPublisher} tracks={p.Tracks.Count}");

var one = await rooms.GetParticipant(new RoomParticipantIdentity {
    Room = roomName, Identity = "kiosk-001" });
```

`ParticipantInfo`: `Sid`, `Identity`, `State` (`Joining` / `Joined` / `Active` / `Disconnected`),
`Tracks` (repeated `TrackInfo`), `Metadata`, `JoinedAt`, `JoinedAtMs`, `Name`, `Version`,
`Permission`, `Region`, `IsPublisher`, `Kind`, `Attributes`, `DataTracks`.

This is your "who is in this session right now" call, useful for a VTM supervisor dashboard.

### RemoveParticipant

```csharp
await rooms.RemoveParticipant(new RoomParticipantIdentity {
    Room = roomName, Identity = "kiosk-001" });
```

Disconnects them and raises `Disconnected` on their client. **They can rejoin with the same token.**
To make removal stick, set `RevokeTokenTs` (a field on `RoomParticipantIdentity`) or issue tokens
with a short TTL.

### MutePublishedTrack

```csharp
await rooms.MutePublishedTrack(new MuteRoomTrackRequest {
    Room = roomName, Identity = "kiosk-001", TrackSid = trackSid, Muted = true });
```

You need the `TrackSid`, which comes from `ParticipantInfo.Tracks`. Server-side muting the kiosk's
microphone while the teller takes a private call is a plausible VTM feature.

### UpdateParticipant

Changes metadata, name, or permissions **without reissuing a token**:

```csharp
await rooms.UpdateParticipant(new UpdateParticipantRequest {
    Room = roomName,
    Identity = "kiosk-001",
    Metadata = updatedMetadataJson,
    Permission = new ParticipantPermission { CanPublish = false, CanSubscribe = true },
});
```

Revoking `CanPublish` at runtime is how you would implement "customer is now in a read-only wait
state".

### SendData

Server-originated data message to the room. For VTM this is the clean way to push state changes
(queue position, "teller is reviewing your document", session-ending countdown) without a second
WebSocket.

```csharp
await rooms.SendData(new SendDataRequest {
    Room = roomName,
    Data = ByteString.CopyFromUtf8(payloadJson),
    Kind = DataPacket.Types.Kind.Reliable,
    DestinationIdentities = { "kiosk-001" },   // omit to broadcast
    Topic = "vtm-state",
});
```

Fields: `Room`, `Data`, `Kind` (`Reliable` / `Lossy`), `DestinationSids`, `DestinationIdentities`,
`Topic`, `Nonce`. The SDK fills `Nonce` for you.

Clients receive this as `RoomEvent.DataReceived` (JS) or `DataReceived` (RTC .NET).

### UpdateRoomMetadata

```csharp
await rooms.UpdateRoomMetadata(new UpdateRoomMetadataRequest {
    Room = roomName, Metadata = statusJson });
```

Broadcasts `RoomMetadataChanged` to everyone in the room.

### ForwardParticipant / MoveParticipant

> ⚠️ **Not implemented on the self-hosted server.** Both return
> `twirp error unknown: not implemented` on LiveKit OSS v1.13.7 — verified, not assumed. They exist
> in the SDK and in the protocol because they are LiveKit **Cloud** features. See
> [PROJECT.md D-018](PROJECT.md#d-018) for what VTM does instead: a supervisor joins the same room
> with a `Hidden` token, and a transfer swaps the teller inside the room rather than moving the
> customer out of it.


`ForwardParticipant` mirrors a participant's tracks into a second room while the source stays put —
a supervisor listening in on a live VTM session. `MoveParticipant` relocates them, so the source
leaves — transferring a customer from a general queue room to a specialist teller's room. Both need
`DestinationRoom` in the grants, which the SDK sets for you from the request.

---

## 5. EgressServiceClient

`livekit-server-sdk-dotnet-main/LivekitApi/EgressServiceClient.cs` — recording and streaming out.
All `Start*` methods use the `RoomRecord` grant.

For VTM this is how you record a teller session for compliance.

| Method | Request | Records |
|---|---|---|
| `StartRoomCompositeEgress` | `RoomCompositeEgressRequest` | The whole room, composited with a layout. **The one you want for VTM.** |
| `StartWebEgress` | `WebEgressRequest` | An arbitrary web page. |
| `StartParticipantEgress` | `ParticipantEgressRequest` | One participant's tracks. |
| `StartTrackCompositeEgress` | `TrackCompositeEgressRequest` | One audio and one video track, muxed. |
| `StartTrackEgress` | `TrackEgressRequest` | A single track, raw. |
| `UpdateLayout` | `UpdateLayoutRequest` | Change layout mid-recording. |
| `UpdateStream` | `UpdateStreamRequest` | Add or remove RTMP outputs mid-stream. |
| `ListEgress` | `ListEgressRequest` to `ListEgressResponse` | Query active and past egress. |
| `StopEgress` | `StopEgressRequest` | Stop one. |

```csharp
EgressInfo info = await egress.StartRoomCompositeEgress(new RoomCompositeEgressRequest {
    RoomName = "vtm-session-42",
    Layout   = "speaker",
    File     = new EncodedFileOutput {
        FileType = EncodedFileType.Mp4,
        Filepath = "vtm/{room_name}-{time}.mp4",
    },
});
// keep info.EgressId to stop it later
await egress.StopEgress(new StopEgressRequest { EgressId = info.EgressId });
```

> **Egress requires a separate `livekit-egress` service** (plus Redis) running alongside
> `livekit-server`. It is not part of `livekit-server.exe`. The current VTM setup does not have it,
> so these calls will fail until it is deployed. Plan for it early if compliance recording is a
> requirement — it is a meaningful piece of infrastructure, not a flag.

---

## 6. IngressServiceClient

`livekit-server-sdk-dotnet-main/LivekitApi/IngressServiceClient.cs` — pulling external streams
*into* a room (RTMP, WHIP, or a URL).

| Method | Request to Response |
|---|---|
| `CreateIngress` | `CreateIngressRequest` to `IngressInfo` |
| `UpdateIngress` | `UpdateIngressRequest` to `IngressInfo` |
| `ListIngress` | `ListIngressRequest` to `ListIngressResponse` |
| `DeleteIngress` | `DeleteIngressRequest` to `IngressInfo` |

`CreateIngress` uses the `IngressAdmin` grant; the others use `RoomAdmin`.

Also requires a separate `livekit-ingress` service. Probably not needed for VTM unless you want to
inject a pre-recorded welcome video or a branch CCTV feed into a session.

---

## 7. SipServiceClient & AgentDispatchServiceClient

### SipServiceClient

`livekit-server-sdk-dotnet-main/LivekitApi/SipServiceClient.cs` — 16 methods bridging telephony
into rooms. Relevant only if VTM needs a phone fallback ("press 1 to speak to a teller").

Trunks: `CreateSIPInboundTrunk`, `CreateSIPOutboundTrunk`, `GetSIPInboundTrunk`,
`GetSIPOutboundTrunk`, `ListSIPInboundTrunk`, `ListSIPOutboundTrunk`, `UpdateSIPInboundTrunk`,
`UpdateSIPOutboundTrunk`, `DeleteSIPTrunk`, `ListSIPTrunk` *(deprecated)*.

Dispatch rules: `CreateSIPDispatchRule`, `ListSIPDispatchRule`, `UpdateSIPDispatchRule`,
`DeleteSIPDispatchRule`.

Participants: `CreateSIPParticipant` (dial out into a room),
`TransferSIPParticipant` (warm transfer).

Uses `SIPGrants { Admin = true }` for management and `{ Call = true }` for outbound calls.
Needs the separate `livekit-sip` service.

### AgentDispatchServiceClient

`livekit-server-sdk-dotnet-main/LivekitApi/AgentDispatchServiceClient.cs` — 3 methods, all
`RoomAdmin` + `Room`:

| Method | Request to Response |
|---|---|
| `CreateDispatch` | `CreateAgentDispatchRequest` to `AgentDispatch` |
| `DeleteDispatch` | `DeleteAgentDispatchRequest` to `AgentDispatch` |
| `ListDispatch` | `ListAgentDispatchRequest` to `ListAgentDispatchResponse` |

Dispatches a LiveKit Agent (Python or Node worker) into a room. If VTM ever grows an AI assistant
that greets the customer before a human teller picks up, this is the hook.

---

## 8. WebhookReceiver

`livekit-server-sdk-dotnet-main/LivekitApi/WebhookReceiver.cs`

The LiveKit server can POST events to your API. This is the **push** counterpart to polling
`ListParticipants`, and genuinely useful for VTM session state tracking.

```csharp
var receiver = new WebhookReceiver(apiKey, apiSecret);

// in a FastEndpoints handler - read the RAW body, not a deserialised model
string body = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
string auth = HttpContext.Request.Headers["Authorization"]!;

WebhookEvent evt = receiver.Receive(body, auth);
// evt.Event, evt.Room, evt.Participant, evt.EgressInfo, evt.Id, evt.CreatedAt
```

`Receive` verifies the JWT **and** checks that the SHA-256 of the body matches the `sha256` claim,
so a tampered body is rejected. Both checks are skipped if you pass `skipAuth: true`. Don't, except
in tests.

The body must be the **raw, unmodified string**. Any middleware that re-serialises the request will
break the checksum.

Events you get: `room_started`, `room_finished`, `participant_joined`, `participant_left`,
`track_published`, `track_unpublished`, `egress_started`, `egress_updated`, `egress_ended`,
`ingress_started`, `ingress_ended`.

Enable in `livekit.yaml`:

```yaml
webhook:
  api_key: devkey
  urls:
    - http://<your-api-host>:5065/api/webhooks/livekit
```

For VTM, `participant_joined`, `participant_left` and `room_finished` are what you would use to
drive session state and call duration in your own database, without the kiosk or teller having to
report it — and without trusting them to.

---

## 9. Error handling

Everything throws `Twirp.Exception` (note: `Twirp` is in the global namespace):

```csharp
try
{
    var room = await rooms.CreateRoom(new CreateRoomRequest { Name = roomName });
}
catch (Twirp.Exception ex)
{
    // ex.Type is Twirp.ErrorCode, ex.Message is the server's message
    var status = ex.Type switch
    {
        Twirp.ErrorCode.Not_Found          => 404,
        Twirp.ErrorCode.Already_Exists     => 409,
        Twirp.ErrorCode.Permission_Denied  => 403,
        Twirp.ErrorCode.Unauthenticated    => 401,
        Twirp.ErrorCode.Invalid_Argument   => 400,
        _                                  => 502,
    };
}
```

Full `ErrorCode` enum: `NoError`, `Canceled`, `Unknown`, `Invalid_Argument`, `Malformed`,
`Deadline_Exceeded`, `Not_Found`, `Bad_Route`, `Already_Exists`, `Permission_Denied`,
`Unauthenticated`, `Resource_Exhausted`, `Failed_Precondition`, `Aborted`, `Out_Of_Range`,
`Unimplemented`, `Internal`, `Unavailable`, `Data_Loss`.

Network failures (server down, DNS, timeout) surface as `HttpRequestException` instead, so handle
both. A VTM API should map `Unavailable` and `HttpRequestException` to 503 so the kiosk can retry
sensibly rather than showing a hard error to a customer.

---

## 10. Concurrency

**This is a real constraint, not a nitpick.** Look at `BaseService` and any client method:

```csharp
// BaseService: one HttpClient per service instance
httpClient = client ?? new HttpClient();

// every method: mutates shared state, THEN sends
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
return await Twirp.ListParticipants(httpClient, request);
```

`DefaultRequestHeaders` belongs to the `HttpClient`, which is shared by every caller of that client
instance. Two concurrent requests interleave like this:

```
Request A: sets Authorization = token(room="branch-01")
Request B: sets Authorization = token(room="branch-99")     <- overwrites A's header
Request A: sends  ----------------------------------------> with B's token
```

Request A now executes against the server carrying B's grants. In a VTM system, that is a teller in
one branch performing an admin action authorised for another branch's room.

**Options, in order of preference:**

1. **Register service clients as transient or scoped**, so each request gets its own `HttpClient`.
   Correct, but a new `HttpClient` per request risks socket exhaustion — pair it with
   `IHttpClientFactory` and pass the client into the constructor.
2. **Serialise access with a `SemaphoreSlim(1,1)`** around each call in your own wrapper. Simple and
   safe, but it makes the client a bottleneck.
3. **Bypass `BaseService` for the hot paths.** Build the `AccessToken` yourself and call
   `Twirp.*` directly with a per-request `HttpRequestMessage` that carries its own
   `Authorization` header. Most work, fully correct, no bottleneck.

Token generation (`AccessToken` / `ToJwt`) has **no shared state** and is safe to call concurrently
from a singleton, which is why the current `LiveKitService` is fine as it stands. The problem only
appears when you add `RoomServiceClient`.

---

## 11. Native AOT

> **Decision (current): Native AOT is OFF for this API.** `<PublishAot>false</PublishAot>`, with
> `PublishReadyToRun` + `SelfContained` set in the publish profile instead. The reasoning is below;
> this section is kept because the constraints still apply if AOT is ever revisited.

The API previously set `<PublishAot>true</PublishAot>`. A real publish
(`dotnet publish -c Release -r win-x64 -p:PublishAot=true`) produces these warnings from this SDK:

```
AccessToken.cs(280): IL2075 - ConvertClaimsKeysToCamelCase(Object):
    'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicProperties'
    in call to 'System.Type.GetProperties()'

AccessToken.cs(247,251): IL2026 + IL3050 - JsonConvert.SerializeObject(Object):
    Newtonsoft.Json relies on reflection / dynamically creating types
```

### Why IL2075 is dangerous

`ToJwt()` builds the `video` and `sip` claims by reflecting over the grants object:

```csharp
private static Dictionary<string, object> ConvertClaimsKeysToCamelCase(object obj)
{
    return obj.GetType()
        .GetProperties()                                    // <- invisible to the trimmer
        .ToDictionary(p => PascalToCamelCase(p.Name), p => p.GetValue(obj));
}
```

The trimmer cannot see that `VideoGrants`'s property getters are needed, so it may remove them.
Nothing fails at build time. At runtime `GetProperties()` returns fewer entries, or none, and the
JWT is emitted with an **empty or partial `video` claim**. The LiveKit server then rejects the
connection, or worse, accepts a participant with no permissions. The symptom is a token that looks
valid but produces "permission denied", or a participant that cannot publish, and nothing in the
logs points at trimming.

### Mitigation

Root the grant types explicitly. In the API project:

```csharp
// somewhere definitely reachable, e.g. LiveKitService's static ctor
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(VideoGrants))]
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(SIPGrants))]
[DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ClaimsModel))]
static LiveKitService() { }
```

Or an ILLink descriptor, referenced from the csproj as `TrimmerRootDescriptor`:

```xml
<linker>
  <assembly fullname="LivekitApi">
    <type fullname="Livekit.Server.Sdk.Dotnet.VideoGrants" preserve="all" />
    <type fullname="Livekit.Server.Sdk.Dotnet.SIPGrants"  preserve="all" />
    <type fullname="Livekit.Server.Sdk.Dotnet.ClaimsModel" preserve="all" />
  </assembly>
</linker>
```

### Verify, do not assume

Warnings disappearing is **not** proof. The only real test is: publish AOT, generate a kiosk token
and a teller token from the published binary, decode both (jwt.io or `TokenVerifier`), and confirm
the `video` claim contains every expected field with the right values. Do this once, then keep it as
a smoke test — it is exactly the kind of thing that silently regresses on an SDK upgrade.

### Other notes

- `Google.Protobuf` emits an IL3050 for `Marshal.SizeOf(Type)` in a `RepeatedField` fast path. Not
  on any path this API uses.
- On Windows the native link step needs the MSVC toolchain. Publishing from a plain shell fails with
  `'vswhere.exe' is not recognized`; publish from a **Developer Command Prompt for VS**, or from the
  Linux Docker build (the `Dockerfile` already installs `clang` and `zlib1g-dev`).
- If AOT turns out to cost more than it is worth here, `PublishTrimmed=false` plus ReadyToRun is a
  perfectly respectable fallback for an API whose startup time is not on the critical path.

---

## 12. What VTM actually needs

Today the API only generates tokens. `_serverUrl` is loaded but unused; no `RoomServiceClient`
exists yet. A realistic build-out, roughly in dependency order:

**Tokens** (done, worth tightening)
- `POST /api/token` — shorten TTL, narrow the teller grants, add `CanPublishSources`.

**Session lifecycle**
- `POST /api/sessions` — `CreateRoom` with `MaxParticipants = 2` and session metadata; return room
  name plus the kiosk token in one round trip.
- `DELETE /api/sessions/{room}` — `DeleteRoom`.
- `GET /api/sessions` — `ListRooms` for a supervisor dashboard.
- `GET /api/sessions/{room}/participants` — `ListParticipants`.

**In-call control (teller-initiated, authorised by your API)**
- `POST /api/sessions/{room}/participants/{identity}/mute` — `MutePublishedTrack`.
- `DELETE /api/sessions/{room}/participants/{identity}` — `RemoveParticipant`.
- `POST /api/sessions/{room}/message` — `SendData` for queue and state pushes to the kiosk.

**Observability**
- `POST /api/webhooks/livekit` — `WebhookReceiver`, writing session start/end and durations to your
  own store. Do this before you need it; reconstructing call history afterwards is painful.

**Later, if compliance requires it**
- Egress endpoints, but deploy `livekit-egress` and Redis first.

Cross-cutting, before any of the above ships: fix the `ServerUrl` and secret configuration, decide
the concurrency strategy from section 10, and settle the AOT question from section 11.
