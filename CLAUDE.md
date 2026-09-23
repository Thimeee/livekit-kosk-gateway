# VTM — LiveKit Video Teller Machine

**Before doing anything in this workspace, read [docs/PROJECT.md](docs/PROJECT.md).**

It holds the current project state, component status, known issues, and an append-only decision log
explaining why things are the way they are. It is the source of truth — not any assistant's memory,
and not this file.

## What this is

A VTM system for a bank. A customer starts a session at a kiosk, a ring goes out to a pool of
tellers, one accepts, and a video call begins. The teller sees the customer's camera, microphone and
kiosk screen, and drives the kiosk application through a defined command set. Media transport is
self-hosted LiveKit; the control plane is a .NET API in `LivekitServerAPI/`.

## Layout

| Path | What |
|---|---|
| `LivekitServerAPI/` | The control API (.NET 10, FastEndpoints) — the code we are building |
| `LivekitServer/` | Self-hosted `livekit-server.exe` + `livekit.yaml` |
| `vtm-kiosk/` | The customer-facing kiosk page (Angular) — one control, everything else is the teller's |
| `vtm-teller/` | The teller console (Angular + MUK UI Kit) |
| `docs/` | Project state, library references, and the kiosk command set |
| `livekit-server-sdk-dotnet-main/`, `client-sdk-js-main/` | **Read-only source checkouts** of the LiveKit SDKs, for reference. Do not edit. |

## Working agreement

- **Update [docs/PROJECT.md](docs/PROJECT.md) as part of the work, not afterwards.** Sections 1–5
  get rewritten to match reality; sections 6–7 are **append-only** — never edit or delete an
  existing entry, add a new one that supersedes it. Every entry is dated.
- Decisions already made and the alternatives already rejected are in PROJECT.md §4 and §6. Do not
  re-propose a rejected approach without new information; if you disagree, argue against the
  recorded reasoning explicitly.
- Known issues are tracked in PROJECT.md §5 as K-1…K-n. Check there before reporting a "new" one.
- The LiveKit libraries are already documented in `docs/01`–`03` against the local source. Read
  those rather than re-deriving or relying on public documentation, which may not match these
  pinned versions.
- **What the teller can make the kiosk do is defined in [docs/06-kiosk-command-set.md](docs/06-kiosk-command-set.md).**
  Add a command there as part of building it, not afterwards — a command set that lives only in the
  code is not a defined command set.

## Constraints worth knowing up front

- **Native AOT is off deliberately** — the LiveKit SDK has reflection paths that fail silently
  under trimming. See PROJECT.md D-003.
- **`RoomServiceClient` is not thread-safe** — it mutates shared request headers. Never register it
  as a singleton. See PROJECT.md K-4.
- **Git repository**, monorepo at this root (D-013). `livekit-server-sdk-dotnet-main/`,
  `client-sdk-js-main/`, `setup-zip/` and build output are ignored. Git records *what* changed;
  PROJECT.md records *why*. Both matter — a commit without a PROJECT.md entry loses the reasoning.
- **`MoveParticipant` and `ForwardParticipant` do not work** on the self-hosted server — "not
  implemented", they are Cloud features. See PROJECT.md D-018 for what replaced them.
- **Verify against the running LiveKit server**, not just by building. Several problems here were
  invisible until a real participant was in a room. `LivekitServerAPI.http` carries the expected
  status for every endpoint.
