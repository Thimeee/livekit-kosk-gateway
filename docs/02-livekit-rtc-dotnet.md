# Livekit.Rtc.Dotnet

**Version** 0.1.4 · **Target** `netstandard2.1` · **Namespace** `LiveKit.Rtc`
**Source** `livekit-server-sdk-dotnet-main/LivekitRtc/`

A real-time **client** SDK for .NET. It lets a .NET process join a LiveKit room as a participant and
publish or subscribe to audio, video and data — the same capability a browser has, but in-process.

```bash
dotnet add package Livekit.Rtc.Dotnet --version 0.1.4
```

> This is **not** a management SDK. It cannot create rooms, list participants or mint tokens. For
> that you need [Livekit.Server.Sdk.Dotnet](01-livekit-server-sdk-dotnet.md). The two are
> complementary and can be referenced side by side.

> **Status: not in current VTM scope.** This library is parked for a later feature — a server-side
> participant that joins a session programmatically (an auto-guide that walks the customer through
> a form, a transcription worker, a recording bot). The kiosk client itself does **not** use it;
> see [section 2](#2-the-wpf-decision). This document will be rewritten around that feature when it
> is picked up.

---

## Contents

1. [How it is built](#1-how-it-is-built)
2. [The WPF decision](#2-the-wpf-decision)
3. [Room lifecycle](#3-room-lifecycle)
4. [Events](#4-events)
5. [Publishing media](#5-publishing-media)
6. [Receiving media](#6-receiving-media)
7. [Rendering video in WPF](#7-rendering-video-in-wpf)
8. [Data, text and files](#8-data-text-and-files)
9. [RPC](#9-rpc)
10. [Participants](#10-participants)
11. [E2EE](#11-e2ee)
12. [Deployment notes](#12-deployment-notes)

---

## 1. How it is built

This is a thin managed wrapper over LiveKit's **Rust client SDK**, reached through a C FFI layer
(`LivekitRtc/Internal/FfiClient.cs`, `FfiHandle.cs`, `NativeMethods.cs`). All WebRTC work — ICE,
DTLS, SRTP, codecs, jitter buffers, simulcast — happens in native code. The C# side sends protobuf
requests across the FFI boundary and receives protobuf events back.

Precompiled native binaries ship for six platforms in `LivekitRtc/runtimes/`:

```
linux-arm64   linux-x64   osx-arm64   osx-x64   win-arm64   win-x64
```

For the VTM kiosk you need `win-x64`. See [section 12](#12-deployment-notes) for what this means at
publish time.

Practical consequences of the FFI design:

- The library is **not trimming- or AOT-friendly** in the way a pure managed library is. Do not
  assume `PublishAot=true` works here; test it before committing.
- Native handles are wrapped in `IDisposable` / `IAsyncDisposable`. Leaking them leaks native
  memory, which will not show up in a managed profiler.
- The package is considerably larger than the server SDK.

---

## 2. The WPF decision

**Read this before building the kiosk on this library.** `Livekit.Rtc.Dotnet` was designed for
*server-side* participants: recording bots, AI agents, transcription workers. Things that process
frames, not things that show them to a person.

It gives you:

- `AudioSource` / `VideoSource` — you **push raw frames in**
- `AudioStream` / `VideoStream` — you **pull raw frames out**

It does **not** give you:

| Missing | What you would have to build |
|---|---|
| Camera capture | Enumerate and open the webcam yourself (Windows `MediaCapture`, DirectShow, `OpenCvSharp`), convert to I420 or RGBA, push into `VideoSource` on a timer |
| Microphone capture | Open the mic (`NAudio` / WASAPI), convert to 16-bit PCM at a fixed sample rate, push into `AudioSource` |
| Speaker playback | Pull `AudioFrame`s from `AudioStream` and feed them into a `NAudio` output device, with your own jitter handling |
| Video rendering | Convert each `VideoFrame` to BGRA and blit into a `WriteableBitmap` every frame (see [section 7](#7-rendering-video-in-wpf)) |
| Device change handling | All of it — hot-plugging a headset, default device changes |
| Echo cancellation / AGC / noise suppression | The browser does this for free. Here you get none of it. |

That last row is the one most often underestimated. A kiosk has a speaker and a microphone in the
same enclosure; without acoustic echo cancellation the teller hears themselves. Browsers apply AEC
automatically via WebRTC's audio pipeline on captured device audio. When you push pre-captured PCM
into `AudioSource`, you are outside that pipeline.

To be precise about where the gap is: the **native FFI layer does support audio processing**. The
generated protos include `NewApmRequest` (`EchoCancellerEnabled`, `GainControllerEnabled`,
`HighPassFilterEnabled`, `NoiseSuppressionEnabled`) and `AudioSourceOptions` (`EchoCancellation`,
`NoiseSuppression`, `AutoGainControl`, `PreferHardware`). The **managed wrapper never uses them** —
`AudioSource`'s constructor builds a `NewAudioSourceRequest` with only type, sample rate, channel
count and queue size, leaving `Options` unset, and no C# type wraps the APM at all
(`LivekitRtc/AudioSource.cs:36`).

So AEC is reachable in principle: fork the wrapper, set `AudioSourceOptions`, or surface the APM.
That is upstream SDK work, not application work, and it is not something to discover halfway
through building a kiosk.

### The two paths

**Path A — WebView2 hosting `livekit-client`**

Embed a `WebView2` control in the WPF window and run the JavaScript SDK inside it
([doc 03](03-livekit-client-js.md)). Chromium handles camera, mic, speakers, AEC, rendering and
device changes.

- Fast to build; the teller and kiosk then share one client implementation and one set of bugs
- Proven path — most .NET kiosk apps that do video calls work this way
- Bridge WPF and JS with `CoreWebView2.PostWebMessageAsJson` and `WebMessageReceived`
- Cost: a Chromium runtime in your process, and native integration (card reader, printer, signature
  pad) has to cross the JS bridge
- Needs the WebView2 Runtime installed on the kiosk, and camera/mic permission granted for the
  embedded origin

**Path B — `Livekit.Rtc.Dotnet` natively**

- Full native control, no browser, direct access to kiosk peripherals
- Lower memory footprint
- Cost: everything in the table above is yours to build and maintain, AEC included

### Decision: Path A

**The VTM kiosk uses WebView2 + `livekit-client`.** For a kiosk whose job is a reliable two-party
video call, WebView2 gets you a working, well-behaved client in days rather than weeks, and the
echo-cancellation problem is solved rather than deferred.

There is no official LiveKit .NET client SDK to weigh against this — LiveKit's own SDK list marks
.NET as *(community)*, and this repository is that community SDK. The official C# client is the
Unity SDK, which is bound to Unity's rendering and audio pipeline and cannot be used from WPF. So
the real choice was only ever between these two paths.

Three WebView2 specifics that a kiosk gets wrong by default:

1. **Secure context.** `getUserMedia` is blocked on `file://`. Serve the app through
   `CoreWebView2.SetVirtualHostNameToFolderMapping("app.vtm.local", folder, Allow)` and navigate to
   `https://app.vtm.local/index.html`.
2. **Permission prompt.** Nobody is standing at a kiosk to click *Allow*. Handle
   `CoreWebView2.PermissionRequested` and grant camera and microphone automatically.
3. **Runtime version.** The Evergreen runtime auto-updates, which is a risk for a deployed fleet.
   Ship a **Fixed Version** runtime with the app.

Native peripherals (card reader, printer, signature pad) cross the boundary via
`PostWebMessageAsJson` / `WebMessageReceived`.

Path B stays documented below in case something later genuinely needs it — frame-level processing,
custom capture hardware, or a strict no-browser policy. The rest of this document covers it
properly, so that choice stays an informed one rather than a default.

---

## 3. Room lifecycle

`livekit-server-sdk-dotnet-main/LivekitRtc/Room.cs`

```csharp
using LiveKit.Rtc;

var room = new Room();

// wire up handlers BEFORE connecting, or you will miss early events
room.Connected    += (s, e) => Console.WriteLine("connected");
room.Disconnected += (s, reason) => Console.WriteLine($"disconnected: {reason}");

await room.ConnectAsync("ws://192.168.1.190:7880", jwtFromYourApi, new RoomOptions {
    AutoSubscribe  = true,
    Dynacast       = false,
    AdaptiveStream = false,
    JoinRetries    = 3,
});

// ... session ...

await room.DisconnectAsync();
room.Dispose();
```

Note the URL scheme: `ws://` or `wss://` here, **not** `http://`. That is the opposite of the
server SDK.

### RoomOptions

| Option | Default | Meaning |
|---|---|---|
| `AutoSubscribe` | `true` | Automatically subscribe to every track published in the room. |
| `Dynacast` | `false` | Server pauses video layers nobody is consuming. Saves publisher CPU and bandwidth. |
| `AdaptiveStream` | `false` | Adjusts subscribed quality to the renderer. Meaningful only if you report element sizes; less useful in a custom .NET renderer. |
| `JoinRetries` | `3` | Join attempts before giving up. |
| `E2EE` | `null` | `E2EEOptions`, see [section 11](#11-e2ee). |

For a two-party VTM call, the defaults are fine. `Dynacast` matters more in large rooms.

### Room properties

`Sid`, `Name`, `Metadata`, `NumParticipants`, `NumPublishers`, `ActiveRecording`, `CreationTime`,
`DepartureTimeout`, `EmptyTimeout`, `ConnectionState`, `IsConnected`, `LocalParticipant`,
`RemoteParticipants` (an `IReadOnlyDictionary<string, RemoteParticipant>` keyed by identity),
`E2EEManager`.

### Methods

| Method | Purpose |
|---|---|
| `ConnectAsync(url, token, options?)` | Join. |
| `DisconnectAsync()` | Leave gracefully. |
| `Disconnect()` | Synchronous variant. |
| `GetRtcStatsAsync()` | Returns `RtcStats` with `PublisherStats` and `SubscriberStats`. Your connection-quality diagnostics. |
| `RegisterTextStreamHandler(topic, handler)` / `Unregister...` | Incoming text streams. |
| `RegisterByteStreamHandler(topic, handler)` / `Unregister...` | Incoming byte streams and files. |
| `Dispose()` | Release native resources. **Always call it.** |

---

## 4. Events

All events are standard .NET `EventHandler<T>`. Handlers are invoked from SDK threads, so
**marshal to the UI thread** before touching WPF:
`Application.Current.Dispatcher.Invoke(...)`. Forgetting this is the single most common bug when
wiring this SDK into WPF.

### Connection

| Event | Payload |
|---|---|
| `Connected` | — |
| `Disconnected` | `Proto.DisconnectReason` |
| `Reconnecting` | — |
| `Reconnected` | — |
| `ConnectionStateChanged` | `Proto.ConnectionState` |
| `Moved` | `RoomInfo` — server moved you to another room |
| `TokenRefreshed` | `string` |

### Participants

| Event | Payload |
|---|---|
| `ParticipantConnected` | `Participant` |
| `ParticipantDisconnected` | `Participant` |
| `ParticipantMetadataChanged` | `Participant` |
| `ParticipantNameChanged` | `Participant` |
| `ParticipantAttributesChanged` | `ParticipantAttributesChangedEventArgs` |
| `ParticipantEncryptionStatusChanged` | `ParticipantEncryptionStatusChangedEventArgs` |
| `ActiveSpeakersChanged` | `ActiveSpeakersChangedEventArgs` |
| `ConnectionQualityChanged` | `ConnectionQualityChangedEventArgs` |

### Tracks

| Event | Payload |
|---|---|
| `TrackPublished` / `TrackUnpublished` | `TrackPublishedEventArgs` |
| `TrackSubscribed` / `TrackUnsubscribed` | `TrackSubscribedEventArgs` |
| `TrackSubscriptionFailed` | `TrackSubscriptionFailedEventArgs` |
| `TrackMuted` / `TrackUnmuted` | `TrackMutedEventArgs` |
| `LocalTrackPublished` / `LocalTrackUnpublished` | `LocalTrackPublishedEventArgs` |
| `LocalTrackSubscribed` | `LocalTrackSubscribedEventArgs` |

### Room and data

| Event | Payload |
|---|---|
| `RoomMetadataChanged` | `string` |
| `RoomUpdated` | `RoomInfo` |
| `RoomSidChanged` | `string` |
| `DataReceived` | `DataReceivedEventArgs` |
| `ChatMessageReceived` | `ChatMessageReceivedEventArgs` |
| `TranscriptionReceived` | `TranscriptionReceivedEventArgs` |
| `SipDtmfReceived` | `SipDtmfReceivedEventArgs` |
| `E2EEStateChanged` | `E2EEStateChangedEventArgs` |

`TrackSubscribed` is the one that drives a kiosk UI: it fires when the teller's camera or mic
arrives, and that is where you start a `VideoStream` or `AudioStream`.

---

## 5. Publishing media

### Audio

```csharp
var audioSource = new AudioSource(sampleRate: 48000, numChannels: 1, queueSizeMs: 1000);
var audioTrack  = LocalAudioTrack.Create("microphone", audioSource);
await room.LocalParticipant!.PublishTrackAsync(audioTrack, new TrackPublishOptions {
    Source = Proto.TrackSource.SourceMicrophone,
});

// then, continuously, from your capture callback:
var frame = AudioFrame.Create(48000, 1, samplesPerChannel: 480);   // 10 ms
capturedPcm.CopyTo(frame.DataMutable);
await audioSource.CaptureFrameAsync(frame);
```

`AudioFrame` holds **16-bit signed PCM**, interleaved, as `Span<short>`. Useful members:
`Data`, `DataMutable`, `DataArray`, `DataBytes`, `SampleRate`, `NumChannels`, `SamplesPerChannel`,
`TotalSamples`, `Duration`, `GetSample(ch, i)`, `SetSample(ch, i, v)`, `Clone()`, `ToWavBytes()`.

`AudioSource` also exposes `QueuedDuration`, `ClearQueue()`, `WaitForPlayoutAsync()` and a
`CreateTrack(name)` shortcut. Watch `QueuedDuration`: if it grows steadily you are pushing faster
than real time and latency will climb.

**Where do the PCM samples come from?** You. `NAudio`'s `WasapiCapture` is the usual answer on
Windows. You are responsible for resampling to your chosen rate and for the absence of echo
cancellation.

### Video

```csharp
var videoSource = new VideoSource(width: 1280, height: 720);
var videoTrack  = LocalVideoTrack.Create("camera", videoSource);
await room.LocalParticipant!.PublishTrackAsync(videoTrack, new TrackPublishOptions {
    Source        = Proto.TrackSource.SourceCamera,
    VideoCodec    = Proto.VideoCodec.Vp8,
    VideoEncoding = new VideoEncodingOptions { MaxBitrate = 1_500_000, MaxFramerate = 30 },
    Simulcast     = true,
});

// per captured frame:
var frame = VideoFrame.Create(1280, 720, VideoBufferType.I420);
// ... fill frame.DataMutable ...
videoSource.CaptureFrame(frame);
```

### TrackPublishOptions

`VideoEncoding`, `AudioEncoding`, `VideoCodec`, `Dtx`, `Red`, `Simulcast` (default `true`),
`Source`, `Stream`, `PreconnectBuffer`, `FrameMetadataFeatures`, `ScalabilityMode`,
`VideoEncoder`, `DegradationPreference`.

Set `Source` correctly. It is what lets the other side distinguish a camera from a screen share,
and it is what `CanPublishSources` in the token is checked against.

### Unpublishing

```csharp
await room.LocalParticipant!.UnpublishTrackAsync(trackSid);
```

---

## 6. Receiving media

Both stream types implement `IAsyncEnumerable<T>`, `IDisposable` and `IAsyncDisposable`.

```csharp
room.TrackSubscribed += async (s, e) =>
{
    if (e.Track.Kind == Proto.TrackKind.KindVideo)
    {
        using var stream = new VideoStream(e.Track, VideoBufferType.Bgra);
        await foreach (var frameEvent in stream)
        {
            // frameEvent.Frame, .TimestampUs, .Rotation
            RenderToWpf(frameEvent.Frame);
        }
    }
};
```

Factory helpers: `VideoStream.FromTrack(track, format?)`,
`VideoStream.FromParticipant(participant, source, format?)`, and the equivalents on `AudioStream`.

Alternatives to `await foreach`: `ReadAsync(ct)` returns `ValueTask<VideoFrameEvent?>`, and
`TryRead(out ...)` is non-blocking.

`AudioStream` additionally takes a target `sampleRate` / `numChannels` (it resamples for you) and
`NoiseCancellationOptions` if a native noise-cancellation module is available.

Each stream runs its own consumption loop. Dispose it when the track goes away, or you leak a native
handle and a task.

### VideoFrame

`Width`, `Height`, `Type`, `Data` (`ReadOnlySpan<byte>`), `DataMutable`, `DataBytes`,
`GetPlane(int)`, `Convert(targetType, flipY)`, `Clone()`, and the statics
`GetBufferSize(type, w, h)` and `GetStride(type, w)`.

`VideoBufferType`: `Rgba`, `Abgr`, `Argb`, `Bgra`, `Rgb24`, `I420`, `I420A`, `I422`, `I444`,
`I010`, `Nv12`.

---

## 7. Rendering video in WPF

There is no LiveKit video control for WPF. You convert frames and blit them yourself. The standard
approach:

```csharp
// once, on the UI thread, when you learn the frame size
var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
videoImage.Source = bitmap;   // an <Image> in your XAML

// per frame, from the stream loop
void RenderToWpf(VideoFrame frame)
{
    var bgra = frame.Type == VideoBufferType.Bgra
        ? frame
        : frame.Convert(VideoBufferType.Bgra);

    Application.Current.Dispatcher.Invoke(() =>
    {
        bitmap.Lock();
        try
        {
            Marshal.Copy(bgra.DataBytes, 0, bitmap.BackBuffer, bgra.DataBytes.Length);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, bgra.Width, bgra.Height));
        }
        finally { bitmap.Unlock(); }
    });
}
```

Things that will bite you here:

- **Ask the stream for BGRA up front** (`new VideoStream(track, VideoBufferType.Bgra)`) so the
  native side converts, rather than calling `Convert` on every frame in managed code.
- `Dispatcher.Invoke` **blocks** the stream loop. At 30 fps with a busy UI thread this backs up.
  `InvokeAsync` with a drop-if-busy flag is usually better: showing the newest frame and discarding
  a late one is correct behaviour for live video.
- Resolution can change mid-call (simulcast layer switches, `AdaptiveStream`). Recreate the
  `WriteableBitmap` when `frame.Width`/`Height` change.
- `WriteableBitmap` is CPU-side. It is adequate for one or two 720p streams. Beyond that, look at
  D3DImage or a SharpDX/Vortice surface.
- Honour `frameEvent.Rotation` if the source can be a mobile camera. For a fixed kiosk camera you
  can ignore it.

---

## 8. Data, text and files

For VTM this is how the kiosk and teller exchange non-media state without a second server
connection.

```csharp
// send
await room.LocalParticipant!.PublishDataAsync(
    Encoding.UTF8.GetBytes(payloadJson),
    new DataPublishOptions {
        Reliable = true,
        Topic = "vtm-state",
        DestinationIdentities = new[] { "teller-07" },   // omit to broadcast
    });

// receive
room.DataReceived += (s, e) => { /* e.Data, e.Participant, e.Topic, e.Kind */ };
```

`Reliable = true` guarantees delivery and ordering (use for state changes and commands).
`Reliable = false` is lossy and lower latency (use for cursor positions or telemetry).

Higher-level helpers on `LocalParticipant`:

| Method | Use |
|---|---|
| `SendTextAsync(text, options)` | One-shot text on a topic |
| `StreamTextAsync(options)` | Returns a `TextStreamWriter` for chunked text |
| `SendFileAsync(path, options)` | File transfer |
| `StreamBytesAsync(options)` | Returns a `ByteStreamWriter` |
| `SendChatMessageAsync(text)` / `EditChatMessageAsync(...)` | Built-in chat with edit support |
| `PublishTranscriptionAsync(transcription)` | Publish transcription segments |
| `PublishSipDtmfAsync(code, digit)` | DTMF tones |

Receive streams by registering handlers on the room:

```csharp
room.RegisterByteStreamHandler("vtm-documents", async (reader, participantIdentity) => {
    // reader gives you the incoming bytes
});
```

A VTM use case that fits this well: the teller pushes a document for the customer to review on the
kiosk screen, over a byte stream on a dedicated topic, rather than through a separate upload
service.

---

## 9. RPC

Request/response between participants, with timeouts and typed errors. Cleaner than hand-rolling a
correlation id over data messages.

```csharp
// on the kiosk - expose a method the teller can call
room.LocalParticipant!.RegisterRpcMethod("get-kiosk-status", async (RpcInvocationData data) =>
{
    // data.RequestId, data.CallerIdentity, data.Payload, data.ResponseTimeout
    return statusJson;      // the response payload
});

// calling a method on another participant
string result = await room.LocalParticipant!.PerformRpcAsync(
    destinationIdentity: "kiosk-001",
    method: "get-kiosk-status",
    payload: "",
    responseTimeout: 5.0);        // seconds; default 10
```

Note the .NET signature is positional — `PerformRpcAsync(string destinationIdentity, string method,
string payload, double? responseTimeout = null)` — unlike the JS SDK, which takes an options object.
Handlers are `Task<string> RpcMethodHandler(RpcInvocationData data)`, and failures throw `RpcError`
(with `Code`, `Message`, `RpcData`).

`UnregisterRpcMethod(method)` removes a handler. The JS SDK has the equivalent API
([doc 03 section 9](03-livekit-client-js.md#9-rpc)), so kiosk and teller can call each other
symmetrically — "is the card reader ready?", "print this receipt", "capture a signature".

---

## 10. Participants

`livekit-server-sdk-dotnet-main/LivekitRtc/Participant.cs`

`Participant` (base): `Sid`, `Identity`, `Name`, `Metadata`, `Kind`, `TrackPublications`,
`GetTrackPublication(sid)`.

`LocalParticipant` adds publishing, data, RPC, plus:

| Method | Purpose |
|---|---|
| `SetMetadataAsync(string)` | Requires `CanUpdateOwnMetadata` in the token |
| `SetNameAsync(string)` | Change display name |
| `SetAttributesAsync(Dictionary<string,string>)` | Change attributes |
| `SetTrackSubscriptionPermissions(...)` | Control who may subscribe to your tracks |

`RemoteParticipant` represents everyone else. Reach them through
`room.RemoteParticipants[identity]`.

---

## 11. E2EE

```csharp
var room = new Room();
await room.ConnectAsync(url, token, new RoomOptions {
    E2EE = new E2EEOptions { /* key provider configuration */ }
});
room.E2EEStateChanged += (s, e) => { /* per-participant encryption state */ };
```

Available through `E2EEManager` (`room.E2EEManager`) — see `LivekitRtc/E2EE.cs`.

Worth knowing for a banking VTM, but understand the trade first: E2EE means the media is opaque to
the server, which **breaks server-side recording and egress**. If compliance requires recorded
sessions, E2EE and that requirement are in direct conflict. Decide which one wins before building
either.

---

## 12. Deployment notes

**Native binaries.** `runtimes/win-x64/native/` must reach the kiosk. Verify after publish that the
native `.dll` sits next to your executable. A self-contained single-file publish extracts it at
runtime; a plain framework-dependent publish copies it. If it is missing, the failure is a
`DllNotFoundException` at the first `new Room()`, not at startup.

**AOT and trimming.** Do not assume `PublishAot=true` works with this library. The FFI layer plus
protobuf reflection is exactly the shape that trimming breaks. If the kiosk needs AOT, prove it with
a real end-to-end call from the published binary before you rely on it. For a WPF desktop app there
is usually no reason to want AOT anyway.

**Threading.** Every event handler and every stream loop runs off the UI thread. All WPF access
must go through the `Dispatcher`.

**Disposal.** `Room`, `VideoStream`, `AudioStream`, `AudioSource`, `VideoSource` and the frame
types all hold native handles. Dispose them deterministically — on window close, on disconnect, on
`TrackUnsubscribed`. The GC will not save you here.

**Firewall.** The kiosk needs outbound UDP to the server's `50000-60000` range, plus TCP `7880`
(signalling) and `7881` (TCP fallback). If UDP is blocked the connection will appear to succeed and
then show no media — the classic symptom.
