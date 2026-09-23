# VTM — Video Teller Machine

A customer walks up to a kiosk in a bank branch and presses one button. A ring goes out to every
available teller; one accepts, and a video call begins. The teller sees the customer, hears them,
can view their kiosk screen, and drives the kiosk from their own console.

Media runs on **self-hosted LiveKit**. The control plane is a **.NET 10** API. Both front ends are
**Angular 21**, zoneless.

> **The kiosk has one control.** Not mute, not camera, not hang up — just *I'm finished*, and that
> one asks rather than acts. Nobody is standing at a kiosk to manage a call, so the teller holds
> everything else. That decision shapes most of what follows.

---

## What is here

| | |
|---|---|
| `LivekitServerAPI/` | The control plane — .NET 10, FastEndpoints, EF Core, SQL Server. 22 routes: auth, sessions, queue, participant control, kiosk enrolment. |
| `vtm-kiosk/` | The customer-facing page. Angular 21, one button. |
| `vtm-teller/` | The teller console. Angular 21 on [MUK UI Kit](https://www.npmjs.com/package/@thimi/muk-kit). Queue, ring, call, and every control. |
| `vtm-shared/` | The teller ↔ kiosk contract and its plumbing. One copy, imported by both apps. |
| `LivekitServer/` | Self-hosted `livekit-server` 1.13.7 and its config. |
| `docs/` | Library references, the command set, and the decision log. |

## How the pieces talk

```
  kiosk ──────────┐                        ┌────────── teller console
   (Angular)      │                        │            (Angular)
        │         ▼                        ▼                 │
        │   ┌──────────────┐        ┌─────────────┐          │
        │   │ LiveKit      │        │ .NET API    │──SignalR─┘
        │   │ (media +     │◄───────│ (sessions,  │   the ring
        │   │  data chan.) │        │  queue,     │
        │   └──────┬───────┘        │  control)   │
        │          │                └─────────────┘
        └──────────┴── RPC over the data channel ─────────────┘
                       (teller drives the kiosk)
```

Three paths, chosen per job rather than uniformly:

- **The ring** goes over **SignalR**, grouped by branch. A teller whose feed has dropped stops
  being rung, so the console says which of its two connections is up.
- **Mute and camera** go through the **API to LiveKit's server**. Server-side mute is
  authoritative and still works when the kiosk page is wedged — which is when a teller most needs
  it.
- **Screen share and the exit request** go **browser to browser over RPC**, because a server
  cannot create a media track and only the kiosk's own page can start a capture.

See [docs/06-kiosk-command-set.md](docs/06-kiosk-command-set.md) for the contract.

## Running it

You need SQL Server, the LiveKit server, and the API. Then:

```bash
# the API
cd LivekitServerAPI/LivekitServerAPI/LivekitServerAPI
cp appsettings.Development.template.json appsettings.Development.json   # fill in the blanks
dotnet run

# the two front ends
cd vtm-teller && npm install && npm start     # http://localhost:4200
cd vtm-kiosk  && npm install && npm start     # http://localhost:4201
```

The kiosk must be **enrolled** before it will do anything, and must be **launched in kiosk mode**
or the teller's "view their screen" will sit waiting on a picker nobody is there to click. Both are
covered in [vtm-kiosk/README.md](vtm-kiosk/README.md).

**No credential is committed.** `appsettings.json` ships every secret value empty and the API
refuses to start rather than half-working without them.

## Documentation

**Start with [docs/PROJECT.md](docs/PROJECT.md)** — current state, known issues, and an
append-only log of 31 dated decisions with the reasoning behind each. It is the source of truth for
*why* things are the way they are.

| | |
|---|---|
| [01](docs/01-livekit-server-sdk-dotnet.md) · [02](docs/02-livekit-rtc-dotnet.md) · [03](docs/03-livekit-client-js.md) | The LiveKit SDKs, written against the pinned source rather than public docs |
| [04](docs/04-api-surface.md) · [05](docs/05-data-model.md) | API surface and data model |
| [06](docs/06-kiosk-command-set.md) | What the teller can make the kiosk do, and how to add a command |

## A few things this repo learned the hard way

Each of these was a real bug, found by measuring rather than reading. They are written up in the
decision log; they are here because they are the kind of thing that catches everyone.

- **`track.attach()` with no argument creates an orphan.** It makes an `<audio>` element and never
  appends it. Both apps shipped it and had no audio at all. Give audio a real element.
- **LiveKit's data channel is not pub/sub.** No subscriptions, no retention, no replay — `topic` is
  a label every participant receives and filters itself. State has an owner and is *asked for*.
- **`SignalReconnecting` is not `Reconnecting`.** A network blip raises the first, a full reconnect
  the second. Listening for one leaves people staring at a frozen picture.
- **`server-leave` does not heal.** The client ends up `Disconnected` and stays there. Both sides
  rejoin deliberately rather than dropping the customer.
- **A muted video track stays subscribed**, so the tile keeps rendering its last frame. Camera off
  has to be read from the publication's mute state.

## Status

Working: LiveKit server, the API end to end, the queue and ring, both front ends, the command
channel with state resync and rejoin.

Not yet: webhooks, teller management, recording, TLS, and the WPF/WebView2 shell that card readers,
printers and multi-screen selection need. Tracked in
[PROJECT.md §3](docs/PROJECT.md#3-component-status).
