# livekit-client (JavaScript / TypeScript) — for the Angular teller app

**Version** 2.22.3 · **Package** `livekit-client` · **Source** `client-sdk-js-main/`

The browser client SDK. This is what the Angular teller application uses to join a VTM session,
publish the teller's camera and microphone, and render the customer's video.

```bash
npm install livekit-client
```

Ships ESM (`livekit-client.esm.mjs`) and UMD builds, with TypeScript definitions. No extra
configuration is needed for Angular's build system.

Unlike the two .NET libraries, this one **runs in a browser**, which means the browser gives you
camera and microphone capture, device selection, echo cancellation, noise suppression, automatic
gain control, and video rendering — for free. That is the whole reason the teller side is the easy
side.

---

## Contents

1. [Connecting](#1-connecting)
2. [RoomOptions](#2-roomoptions)
3. [Publishing](#3-publishing)
4. [Rendering remote media](#4-rendering-remote-media)
5. [Events](#5-events)
6. [An Angular service](#6-an-angular-service)
7. [Autoplay](#7-autoplay)
8. [Data messages](#8-data-messages)
9. [RPC](#9-rpc)
10. [Devices](#10-devices)
11. [Connection quality and diagnostics](#11-connection-quality-and-diagnostics)
12. [Cleanup checklist](#12-cleanup-checklist)

---

## 1. Connecting

```ts
import { Room, RoomEvent, Track } from 'livekit-client';

const room = new Room({
  adaptiveStream: true,
  dynacast: true,
});

// register handlers BEFORE connect, or early events are lost
room.on(RoomEvent.Connected, () => console.log('connected'));

await room.connect('ws://192.168.1.190:7880', tokenFromYourApi);
await room.localParticipant.enableCameraAndMicrophone();
```

The token comes from `POST /api/token` on `LivekitServerAPI`. The Angular app never sees the LiveKit
API secret.

`room.prepareConnection(url, token)` can be called early — on page load, before the teller clicks
"answer" — to warm up DNS, TLS and the signalling path. On a VTM teller console where every second
of answer latency is visible to a waiting customer, this is worth doing.

### ConnectionState

`Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `SignalReconnecting`.
Read `room.state`, or subscribe to `RoomEvent.ConnectionStateChanged`.

---

## 2. RoomOptions

| Option | Default | Meaning |
|---|---|---|
| `adaptiveStream` | `false` | Adjusts subscribed video quality to the size of the attached element, and pauses when it is not visible. **Turn this on** — a teller window showing a small customer thumbnail should not pull 1080p. |
| `dynacast` | `false` | Pauses publishing of video layers nobody consumes. Saves the teller's upstream bandwidth. |
| `audioCaptureDefaults` | — | `AudioCaptureOptions` — device id, echo cancellation, noise suppression, AGC. |
| `videoCaptureDefaults` | — | `VideoCaptureOptions` — device id, resolution, frame rate, facing mode. |
| `publishDefaults` | — | `TrackPublishDefaults` — codec, bitrate, simulcast, DTX, RED. |
| `audioOutput` | — | Output device id (speaker selection). |
| `stopLocalTrackOnUnpublish` | `true` | Stop the underlying `MediaStreamTrack` on unpublish, which turns the camera light off. |
| `reconnectPolicy` | `DefaultReconnectPolicy` | Backoff strategy. |
| `disconnectOnPageLeave` | `true` | Disconnect on `pagehide` / `beforeunload`. |
| `webAudioMix` | `false` | Mix all audio through Web Audio. Helps with some autoplay problems. |
| `encryption` | — | `E2EEOptions`. Note it breaks server-side recording. |
| `e2ee` | — | Deprecated alias for `encryption`. |

A reasonable teller-side configuration:

```ts
const room = new Room({
  adaptiveStream: true,
  dynacast: true,
  audioCaptureDefaults: {
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true,
  },
  videoCaptureDefaults: {
    resolution: { width: 1280, height: 720, frameRate: 30 },
  },
  publishDefaults: {
    videoCodec: 'vp8',           // widest compatibility
    simulcast: true,
    dtx: true,
  },
});
```

`vp8` is the safe default. `vp9` and `av1` compress better but cost CPU and have patchier support;
if the kiosk side is a native .NET client, confirm codec support on both ends before switching.

---

## 3. Publishing

The convenience toggles handle capture, publish and unpublish in one call:

```ts
await room.localParticipant.setCameraEnabled(true);
await room.localParticipant.setMicrophoneEnabled(true);
await room.localParticipant.setScreenShareEnabled(true);   // teller sharing a form with the customer
await room.localParticipant.enableCameraAndMicrophone();   // both at once
```

These are what you bind your mute buttons to. Each returns the publication (or `undefined`).

For finer control, create tracks first:

```ts
import { createLocalTracks, createLocalVideoTrack, createLocalAudioTrack } from 'livekit-client';

const tracks = await createLocalTracks({
  audio: { echoCancellation: true, noiseSuppression: true },
  video: { resolution: { width: 1280, height: 720 } },
});
for (const track of tracks) {
  await room.localParticipant.publishTrack(track, { source: track.source });
}
```

Also on `localParticipant`: `publishTrack`, `unpublishTrack`, `unpublishTracks`,
`republishAllTracks`, `createTracks`, `createScreenTracks`, `setMetadata`, `setName`,
`setAttributes`, `setTrackSubscriptionPermissions`.

### Preview before joining

A common teller-console requirement — check your hair before answering:

```ts
const previewTrack = await createLocalVideoTrack();
previewTrack.attach(previewVideoElement);
// later, when answering:
await room.localParticipant.publishTrack(previewTrack);
```

### Track.Source

`Camera`, `Microphone`, `ScreenShare`, `ScreenShareAudio`, `Unknown`.
Always set it — it is how the far side distinguishes a face from a shared screen, and it is what the
token's `CanPublishSources` is validated against.

---

## 4. Rendering remote media

`track.attach()` creates a suitable `<video>` or `<audio>` element; `track.attach(element)` binds to
one you own. `track.detach()` unbinds.

```ts
room.on(RoomEvent.TrackSubscribed, (track, publication, participant) => {
  if (track.kind === Track.Kind.Video) {
    track.attach(this.customerVideoRef.nativeElement);
  } else if (track.kind === Track.Kind.Audio) {
    track.attach();            // no element needed; SDK creates and plays a hidden <audio>
  }
});

room.on(RoomEvent.TrackUnsubscribed, (track) => {
  track.detach();
});
```

> **Do not use `attach()` with no argument for audio.** It calls `document.createElement('audio')`
> and **never appends it**, then plays the orphan. Chrome may suspend such an element, Safari will
> not play one at all, and nothing can reach it afterwards to set volume or an output device. Both
> VTM apps shipped this and had no audio at all; see PROJECT.md D-025. Give the track a real
> element that is in the document:
>
> ```html
> <audio #remoteAudio autoplay></audio>
> ```
> ```ts
> track.attach(this.remoteAudioRef.nativeElement);
> ```

In a two-party VTM call you can also address the customer directly rather than reacting to events:

```ts
const customer = room.getParticipantByIdentity('kiosk-001');
const pub = customer?.getTrackPublication(Track.Source.Camera);
pub?.videoTrack?.attach(videoEl);
```

Local preview after publishing:

```ts
const cameraPub = room.localParticipant.getTrackPublication(Track.Source.Camera);
cameraPub?.videoTrack?.attach(this.selfViewRef.nativeElement);
```

---

## 5. Events

`room.on(RoomEvent.X, handler)`. Subscribe before `connect()`. Full list from
`client-sdk-js-main/src/room/events.ts`:

### Connection

`Connected`, `Reconnecting`, `SignalReconnecting`, `Reconnected`, `Disconnected`,
`ConnectionStateChanged`, `SignalConnected`, `Moved`

### Participants

`ParticipantConnected`, `ParticipantDisconnected`, `ParticipantMetadataChanged`,
`ParticipantNameChanged`, `ParticipantAttributesChanged`, `ParticipantActive`,
`ParticipantPermissionsChanged`, `ParticipantEncryptionStatusChanged`, `ActiveSpeakersChanged`,
`ConnectionQualityChanged`

### Tracks

`TrackPublished`, `TrackUnpublished`, `TrackSubscribed`, `TrackUnsubscribed`,
`TrackSubscriptionFailed`, `TrackMuted`, `TrackUnmuted`, `LocalTrackPublished`,
`LocalTrackUnpublished`, `LocalTrackSubscribed`, `LocalAudioSilenceDetected`,
`TrackStreamStateChanged`, `TrackSubscriptionPermissionChanged`, `TrackSubscriptionStatusChanged`

### Room and data

`RoomMetadataChanged`, `DataReceived`, `ChatMessage`, `TranscriptionReceived`, `SipDTMFReceived`,
`RecordingStatusChanged`, `MetricsReceived`, `EncryptionError`, `DCBufferStatusChanged`

### Devices and playback

`MediaDevicesChanged`, `ActiveDeviceChanged`, `MediaDevicesError`, `AudioPlaybackStatusChanged`,
`VideoPlaybackStatusChanged`

### Data tracks

`DataTrackPublished`, `DataTrackUnpublished`, `LocalDataTrackPublished`,
`LocalDataTrackUnpublished`

### Key callback signatures

```ts
disconnected:            (reason?: DisconnectReason) => void
connectionStateChanged:  (state: ConnectionState) => void
participantConnected:    (participant: RemoteParticipant) => void
participantDisconnected: (participant: RemoteParticipant, reason?: DisconnectReason) => void
trackSubscribed:         (track: RemoteTrack, pub: RemoteTrackPublication, p: RemoteParticipant) => void
trackMuted:              (pub: TrackPublication, participant: Participant) => void
activeSpeakersChanged:   (speakers: Participant[]) => void
connectionQualityChanged:(quality: ConnectionQuality, participant: Participant) => void
dataReceived:            (payload: Uint8Array, p?: RemoteParticipant, kind?: DataPacket_Kind, topic?: string) => void
audioPlaybackChanged:    (playing: boolean) => void
mediaDevicesError:       (error: Error, kind?: MediaDeviceKind) => void
recordingStatusChanged:  (recording: boolean) => void
```

`RecordingStatusChanged` deserves a visible indicator in the teller UI if VTM sessions are recorded
— in most jurisdictions the customer must be told, and the kiosk should show it too.

---

## 6. An Angular service

Wrap the room in an injectable service. Two Angular-specific concerns dominate: **change
detection** and **teardown**.

> **If the app is zoneless** — `provideZonelessChangeDetection()`, the default for new Angular 20+
> apps — **you do not need `NgZone` at all.** A signal write schedules a render by itself, wherever
> it happens. The `zone.run(...)` wrapping below is dead weight there, and `vtm-kiosk` has none of
> it. See PROJECT.md D-023.
>
> The zone-based version is kept because an existing application may still be zone-based, where the
> SDK's events genuinely do not trigger change detection.

```ts
import { Injectable, NgZone, signal } from '@angular/core';
import {
  Room, RoomEvent, Track, ConnectionState,
  type RemoteTrack, type RemoteParticipant,
} from 'livekit-client';

@Injectable({ providedIn: 'root' })
export class VtmRoomService {
  private room?: Room;

  readonly connectionState = signal<ConnectionState>(ConnectionState.Disconnected);
  readonly customerVideo   = signal<RemoteTrack | undefined>(undefined);
  readonly micEnabled      = signal(false);
  readonly camEnabled      = signal(false);
  readonly needsAudioGesture = signal(false);

  constructor(private zone: NgZone) {}

  async join(wsUrl: string, token: string): Promise<void> {
    const room = new Room({
      adaptiveStream: true,
      dynacast: true,
      audioCaptureDefaults: {
        echoCancellation: true, noiseSuppression: true, autoGainControl: true,
      },
    });
    this.room = room;

    room
      .on(RoomEvent.ConnectionStateChanged, (state) =>
        this.zone.run(() => this.connectionState.set(state)))
      .on(RoomEvent.TrackSubscribed, (track: RemoteTrack) =>
        this.zone.run(() => {
          if (track.kind === Track.Kind.Video) this.customerVideo.set(track);
          else track.attach();                       // audio plays itself
        }))
      .on(RoomEvent.TrackUnsubscribed, (track: RemoteTrack) =>
        this.zone.run(() => {
          track.detach();
          if (track.kind === Track.Kind.Video) this.customerVideo.set(undefined);
        }))
      .on(RoomEvent.ParticipantDisconnected, (p: RemoteParticipant) =>
        this.zone.run(() => console.log(`${p.identity} left`)))
      .on(RoomEvent.AudioPlaybackStatusChanged, () =>
        this.zone.run(() => this.needsAudioGesture.set(!room.canPlaybackAudio)))
      .on(RoomEvent.DataReceived, (payload, participant, _kind, topic) =>
        this.zone.run(() => this.handleData(payload, topic)))
      .on(RoomEvent.Disconnected, () =>
        this.zone.run(() => this.reset()));

    await room.connect(wsUrl, token);
    await room.localParticipant.enableCameraAndMicrophone();

    this.zone.run(() => { this.micEnabled.set(true); this.camEnabled.set(true); });
  }

  async toggleMic(): Promise<void> {
    if (!this.room) return;
    const next = !this.micEnabled();
    await this.room.localParticipant.setMicrophoneEnabled(next);
    this.micEnabled.set(next);
  }

  async toggleCam(): Promise<void> {
    if (!this.room) return;
    const next = !this.camEnabled();
    await this.room.localParticipant.setCameraEnabled(next);
    this.camEnabled.set(next);
  }

  /** must be called from a real click handler */
  async startAudio(): Promise<void> {
    await this.room?.startAudio();
    this.needsAudioGesture.set(false);
  }

  send(topic: string, data: unknown): Promise<void> {
    const bytes = new TextEncoder().encode(JSON.stringify(data));
    return this.room!.localParticipant.publishData(bytes, { reliable: true, topic });
  }

  async leave(): Promise<void> {
    await this.room?.disconnect();
    this.reset();
  }

  private handleData(payload: Uint8Array, topic?: string): void {
    const msg = JSON.parse(new TextDecoder().decode(payload));
    // route on topic
  }

  private reset(): void {
    this.room?.removeAllListeners();
    this.room = undefined;
    this.customerVideo.set(undefined);
    this.connectionState.set(ConnectionState.Disconnected);
  }
}
```

In the component, attach the video track to the element and detach on destroy:

```ts
@Component({ /* ... */ })
export class VtmCallComponent implements OnDestroy {
  @ViewChild('customerVideo') customerVideoRef!: ElementRef<HTMLVideoElement>;

  constructor(public vtm: VtmRoomService) {
    effect(() => {
      const track = this.vtm.customerVideo();
      if (track) track.attach(this.customerVideoRef.nativeElement);
    });
  }

  ngOnDestroy(): void {
    void this.vtm.leave();
  }
}
```

Leaving on destroy is not optional. A teller who navigates away without disconnecting leaves a
ghost participant in the room and a camera light on.

---

## 7. Autoplay

Browsers block audio (and sometimes video) playback until the user has interacted with the page.
This affects a teller console that opens in a background tab, and it is a genuinely common support
complaint: "I can see the customer but I can't hear them."

```ts
room.on(RoomEvent.AudioPlaybackStatusChanged, () => {
  if (!room.canPlaybackAudio) {
    // show a button; call startAudio() from its click handler
  }
});
```

`room.startAudio()` **must be called from inside a real user-gesture handler** — a click or a key
press. Calling it from `ngOnInit` or a timer does nothing. The same applies to
`room.startVideo()` and `room.canPlaybackVideo`.

The usual pattern is an overlay: "Click to enable audio", which calls `startAudio()`.

---

## 8. Data messages

Symmetric with the .NET side ([doc 01 section 4](01-livekit-server-sdk-dotnet.md#4-roomserviceclient),
[doc 02 section 8](02-livekit-rtc-dotnet.md#8-data-text-and-files)).

```ts
// send
await room.localParticipant.publishData(
  new TextEncoder().encode(JSON.stringify({ type: 'call-doc', docId: 42 })),
  { reliable: true, topic: 'vtm-state', destinationIdentities: ['kiosk-001'] },
);

// receive - including messages your API pushed via RoomServiceClient.SendData
room.on(RoomEvent.DataReceived, (payload, participant, kind, topic) => {
  const msg = JSON.parse(new TextDecoder().decode(payload));
});
```

Higher-level helpers: `sendText`, `streamText`, `sendFile`, `sendBytes`, `streamBytes`,
`sendChatMessage`, `editChatMessage`, and `room.registerTextStreamHandler(topic, cb)` /
`registerByteStreamHandler(topic, cb)` for receiving.

`reliable: true` for anything that must arrive (state, commands); `false` for high-frequency,
disposable telemetry.

---

## 9. RPC

```ts
// expose a method the kiosk can call
room.localParticipant.registerRpcMethod('teller-status', async (data) => {
  return JSON.stringify({ available: true });
});

// call a method on the kiosk
const result = await room.localParticipant.performRpc({
  destinationIdentity: 'kiosk-001',
  method: 'print-receipt',
  payload: JSON.stringify({ txnId: 'T-9981' }),
  responseTimeout: 10_000,
});
```

Errors come back as `RpcError`, with `RpcInvocationData` on the handler side. The same mechanism
exists in [doc 02 section 9](02-livekit-rtc-dotnet.md#9-rpc), so a WPF kiosk on
`Livekit.Rtc.Dotnet` and an Angular teller can call each other directly — though the .NET
`PerformRpcAsync` takes positional arguments rather than this options object.

Prefer RPC over data messages whenever you need an answer. Timeouts and error propagation are
already handled; rolling your own correlation ids over `publishData` is how you end up with hung
promises.

---

## 10. Devices

```ts
import { Room } from 'livekit-client';

const mics    = await Room.getLocalDevices('audioinput');
const cams    = await Room.getLocalDevices('videoinput');
const outputs = await Room.getLocalDevices('audiooutput');

await room.switchActiveDevice('audioinput',  micDeviceId);
await room.switchActiveDevice('videoinput',  camDeviceId);
await room.switchActiveDevice('audiooutput', speakerDeviceId);

const current = room.getActiveDevice('videoinput');

room.on(RoomEvent.MediaDevicesChanged, () => { /* refresh the picker */ });
room.on(RoomEvent.ActiveDeviceChanged, (kind, deviceId) => { /* update UI */ });
```

`getLocalDevices` is a **static** method on `Room`. Device labels are empty until permission has
been granted, so ask for the camera and mic before populating a settings dropdown, or the teller
sees a list of blank entries.

Note that `audiooutput` switching is not supported in every browser (Firefox in particular).

---

## 11. Connection quality and diagnostics

```ts
room.on(RoomEvent.ConnectionQualityChanged, (quality, participant) => {
  // ConnectionQuality: Excellent | Good | Poor | Lost
});
```

Show this for the customer in the teller UI. "The customer's connection is poor" is far more useful
to a teller than a frozen video with no explanation.

The SDK also ships a pre-flight connection checker:

```ts
import { ConnectionCheck } from 'livekit-client';

const check = new ConnectionCheck(wsUrl, token);
const results = await check.createAndRunAllChecks();
```

It tests WebSocket reachability, WebRTC connectivity, TURN, and publish/subscribe. Worth exposing
behind a "diagnose" button on the teller console, and worth running once when deploying to a new
branch network — it will tell you immediately if the UDP range is blocked.

`room.isRecording` and `RoomEvent.RecordingStatusChanged` tell you whether egress is active.

---

## 12. Cleanup checklist

Angular apps leak LiveKit resources easily because components are destroyed and recreated on
navigation. On every teardown path:

1. `await room.disconnect()` — ends the session server-side and stops local tracks
   (`stopLocalTrackOnUnpublish` defaults to `true`, so camera and mic lights go off)
2. `track.detach()` for every attached track, so elements release their streams
3. `room.removeAllListeners()` — otherwise handlers holding component references keep them alive
4. Do this from `ngOnDestroy`, and also handle `window.beforeunload` if the teller might close the
   tab mid-call (`disconnectOnPageLeave` defaults to `true` and covers most of this)

A quick way to verify: after leaving a call, the browser tab's camera indicator must go out and the
room must disappear from `GET /api/sessions`. If either lingers, something in the list above was
skipped.
