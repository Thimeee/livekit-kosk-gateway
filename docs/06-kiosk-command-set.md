# The kiosk command set

**The contract between the teller console and the kiosk.** CLAUDE.md describes this system as one
where the teller "drives the kiosk application through a defined command set" — this is that
definition. Anything the teller can make the kiosk do belongs here, and nothing belongs here twice.

Current as of **2026-09-22**. Decisions behind it: [PROJECT.md D-028](PROJECT.md#d-028).

---

## Contents

1. [The model](#1-the-model)
2. [Which transport, and why](#2-which-transport-and-why)
3. [Commands over the server](#3-commands-over-the-server)
4. [Commands over RPC](#4-commands-over-rpc)
5. [What the kiosk sends back](#5-what-the-kiosk-sends-back)
6. [Adding a command](#6-adding-a-command)
7. [Not yet built](#7-not-yet-built)

---

## 1. The model

**The kiosk is a terminal, not a participant.** Nobody is standing at it to manage a call. It has
exactly one control — *I'm finished* — and that one asks rather than acts.

Everything else is the teller's. That is not a convenience; it is the reason the system exists. A
customer who could mute themselves, hide their camera and hang up mid-transaction would have four
ways to break their own appointment and no reason to use any of them.

So the rule for anything added later: **if the answer to "who would press this?" is "the customer",
it does not go on the kiosk.**

---

## 2. Which transport, and why

> **The data channel is not pub/sub.** `publishData` is a send; `topic` is a label carried in the
> packet that every participant receives and filters itself. There are no subscriptions, no
> retention and no replay — anything published while a participant was away is gone. State
> therefore has an owner and is **asked for** (`state.get`), never listened for. Measured against
> the pinned SDK; see [D-029](PROJECT.md#d-029).


Two, and the choice is per command rather than uniform.

| | **Server** (API → LiveKit) | **RPC** (browser to browser) |
|---|---|---|
| Path | Teller → `LivekitServerAPI` → LiveKit | Teller page → kiosk page, over the data channel |
| Works when the kiosk page is wedged | **Yes** | No |
| Can create a track | No | **Yes** |
| Audited by the API | Yes | No — see [D-010](PROJECT.md#d-010) |

**Use the server whenever it can do the job.** Server-side mute is authoritative and keeps working
when the kiosk page has stopped responding, which is exactly the moment a teller needs to cut a
microphone.

**Use RPC only when the kiosk itself must act.** A server cannot create a media track, and a
browser will not start a screen capture on behalf of another page. Those are the cases.

> **This does not need [D-011](PROJECT.md#d-011).** That entry records that `Twirp.PerformRpc` is
> unwrapped in the .NET SDK, and it is about the **server** issuing RPC. Teller and kiosk are both
> participants in the same room, and `livekit-client` lets them call each other directly. The
> blocker recorded there does not apply to anything on this page.

---

## 3. Commands over the server

All of these are `POST /api/sessions/{roomName}/participants/{identity}/mute`, which takes an
optional `trackSid` and a `muted` flag. With no `trackSid` it applies to every track the
participant publishes.

| Teller action | Request | Notes |
|---|---|---|
| Mute customer | `{ trackSid: <mic>, muted: true }` | Name the microphone track, or the camera goes too |
| Unmute customer | `{ trackSid: <mic>, muted: false }` | Needs `enable_remote_unmute: true` in `livekit.yaml` — see [K-9](PROJECT.md#5-known-issues) |
| Their camera off | `{ trackSid: <camera>, muted: true }` | |
| Their camera on | `{ trackSid: <camera>, muted: false }` | |

The track sids come from the remote participant in the teller's own `Room`:
`participant.getTrackPublication(Track.Source.Microphone)?.trackSid`.

**Do not set the local signal from the response.** A muted track raises `TrackMuted` on both sides;
let that event drive the UI. A console that shows what it *asked for* rather than what *happened*
will eventually lie to a teller about whether a customer can be heard.

> **A muted video track stays subscribed.** The tile goes on rendering its last frame, so "camera
> off" must be decided from the publication's mute state, not from whether a track exists. This was
> a real bug; see [D-028](PROJECT.md#d-028).

---

## 4. Commands over RPC

Registered by the kiosk on its `localParticipant`, **before `connect()`**, so a command arriving
immediately is not missed. Each returns the string `ok`.

| Method | Payload | Does |
|---|---|---|
| `screenshare.start` | *(empty)* | `setScreenShareEnabled(true)` on the kiosk |
| `screenshare.stop` | *(empty)* | `setScreenShareEnabled(false)` |
| `state.get` | *(empty)* | Returns the kiosk's full `KioskState`. The only honest way for a console to know what it missed. |

Called from the teller as:

```ts
await room.localParticipant.performRpc({
  destinationIdentity: customer.identity,
  method: 'screenshare.start',
  payload: '',
});
```

### The kiosk must be launched in kiosk mode

`getDisplayMedia()` normally needs a user gesture and opens a picker. Measured on this project:

| Kiosk browser | Result with no gesture |
|---|---|
| Plain Chromium | **Opens a picker and waits for a human** — the one thing a kiosk customer cannot do |
| With `--auto-select-desktop-capture-source` | `{"ok":true,"label":"screen:0:0"}` — no gesture, no picker |

So the kiosk browser is launched with:

```
--auto-select-desktop-capture-source="Entire screen"
```

`vtm-kiosk/start-kiosk.cmd` does this. Two things about it are load-bearing:

- **The source name must match exactly.** `"Entire screen"` works; `"Screen 1"` falls through to
  the picker. Both were measured against the installed Chrome, not assumed.
- **The separate `--user-data-dir` is not optional.** A Chrome that is already running ignores new
  flags and hands the window to the existing process, picker and all. This is the most likely
  reason the flag "does not work" for someone.

In production the WPF/WebView2 shell passes this through `AdditionalBrowserArguments`. That is part
of what the shell is for ([D-007](PROJECT.md#d-007)).

A kiosk started without the flag makes `performRpc` time out. The teller is told so plainly rather
than left watching a button that does nothing.

**Choosing *which* screen is not possible from the page.** The flag is applied at launch and a web
page cannot enumerate display sources. One screen works today; a picker for the teller is shell
work.

---

## 5. What the kiosk sends back

| Method | Sent when | The teller does |
|---|---|---|
| `exit.request` | The customer presses *I'm finished* | Shows a standing alert. **The session keeps running.** |
| `screenshare.ended` | The customer pressed the browser's own **Stop sharing** | Toast, plus a note beside the button until the teller asks again. Not sent when the teller stopped it. |

**The browser's "is sharing your screen" bar stays.** It is how the customer knows their screen
is being watched, and hiding it programmatically removes that — the permission system refused it
as security-weakening, which is the right call. Stopping is the customer's right; `screenshare.ended`
is how the teller finds out instead of watching the tile quietly vanish. The kiosk tells the two
stops apart with a flag set only while the teller's own `screenshare.stop` is running, because both
end as the same unpublish.

`exit.request` is a request, deliberately. The teller may be part way through something, and ending their call
from the kiosk side is the kiosk's decision to make least of all.

The kiosk marks its button as sent whether or not the call is acknowledged — the customer has done
their part, and a red error helps nobody standing at a kiosk. The failure is logged to the console,
because silence there is what made an earlier bug hard to find.

---

## 6. Adding a command

Everything goes through `vtm-shared/`, which both apps import. There is one copy of the contract
and one copy of the plumbing.

1. **Can the server do it?** Then do it there. Only reach for RPC when the kiosk must act.
2. **Add it to `vtm-shared/vtm-commands.ts`** — a line in `COMMANDS` with its D-010 class, and a
   line in `Payloads` with its request and response types. Both sides now agree by construction;
   a name on one side only is a compile error.
3. **Name it `noun.verb`.** Grouping by noun keeps the list readable as it grows.
4. **Implement with `channel.handle(name, fn)`** on the side that owns it. Registration and
   re-registration after a reconnect are handled.
5. **Call it with `channel.call(name, req)`.** Errors arrive as `CommandError` with a message fit
   to show someone, and `retryable` when trying again could plausibly work.
6. **Let events drive the UI**, not the return value. A console that shows what it asked for
   rather than what happened will eventually lie to a teller.
7. **Add it to this page**, and record the reasoning in PROJECT.md. A command set that lives only
   in the code is not a defined command set.

### Reconnection

Measured, not assumed ([D-029](PROJECT.md#d-029)):

| | |
|---|---|
| A network blip | The SDK recovers. Handlers survive. Nothing is lost. |
| `server-leave` | **`Disconnected`, and it stays there.** Both apps call `POST /api/sessions/{roomName}/rejoin` with backoff, because the customer is still standing there. |

Anything published while a side was away was **not** delivered, so a side that comes back calls
`state.get` rather than assuming what it last saw is still true.

A side that comes back **after a reload or restart** has lost the room name as well. It asks
`GET /api/sessions/current` and rejoins that room ([D-034](PROJECT.md#d-034)). The other side waits
two minutes for it: the teller sees a countdown, the customer gets a Leave button only once the
two minutes are up. Commands sent to a side that is away fail with an RPC error. Nothing queues
them.

---

## 7. Not yet built

The commands this system was described for, and which nothing implements yet:

| | Needs |
|---|---|
| Fill or clear a form on the kiosk | RPC, plus a kiosk app with forms in it |
| Move the kiosk to a named screen | RPC |
| Read a card | The WPF shell — a browser cannot reach the reader |
| Print | The WPF shell |
| Choose which display to share | The WPF shell, per [§4](#4-commands-over-rpc) |

The first two need nothing that is not already working. The rest are waiting on the shell, which is
tracked in [PROJECT.md §3](PROJECT.md#3-component-status).
