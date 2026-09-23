# VTM Kiosk

The customer-facing screen. Four states: idle, waiting, in call, ended.

## Running it

The API and the LiveKit server must both be up. Then enrol this kiosk and write its
secret to **`public/kiosk.secret`** — a gitignored file, never into tracked source:

```bash
# 1. get an admin token
curl -X POST http://localhost:5065/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"DevAdmin!2026"}'

# 2. enrol K-01 - the secret is shown once and stored only as a hash
curl -X POST http://localhost:5065/api/kiosks/K-01/enroll \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"revokeExisting":true}'
```

Then put the returned `secret` in `public/kiosk.secret`, on its own line:

```bash
echo "<the secret>" > public/kiosk.secret
npm start        # http://localhost:4201
```

A kiosk with no secret file says so on screen rather than showing a Start button
that cannot work.

## Launching it as a kiosk

**A kiosk must not be opened as an ordinary browser window.** `getDisplayMedia()` requires a
user gesture and opens a source picker, so the teller's "View their screen" command sits there
waiting for a click nobody is standing at a kiosk to make.

Use **`start-kiosk.cmd`** (full screen) or **`start-kiosk-windowed.cmd`** (for testing at a
desk). Both pass `--auto-select-desktop-capture-source="Entire screen"`.

Measured against the Chrome installed on this machine:

| Launched | `getDisplayMedia()` with no click |
|---|---|
| Normally | picker opens and waits — **this is what you get if you just open the URL** |
| With the flag | `{"ok":true,"label":"screen:0:0","size":"1280x720"}` |
| With `="Screen 1"` | picker — **the source name has to match exactly** |

The separate `--user-data-dir` in those scripts is not optional: a Chrome that is already
running ignores new flags and hands the window to the existing process, picker and all. That
is the most likely reason the flag looks like it does not work.

In production the WPF/WebView2 shell passes the same argument. See
[docs/06-kiosk-command-set.md](../docs/06-kiosk-command-set.md).


## Why it is a web app

The kiosk ships as a WPF shell hosting WebView2, and this is the page it hosts
(docs/PROJECT.md D-007). Building the page first keeps WPF off the critical path;
adding the shell later changes nothing here. WPF becomes necessary when card
readers and printers do.

## Notes

- The secret lives in a gitignored file only because this is a demo, and because a
  credential that has to be scrubbed before every commit eventually gets committed -
  which is what happened once (PROJECT.md D-026). A real kiosk receives its secret at
  enrolment and stores it where the machine can protect it: DPAPI or the TPM.
- Zoneless. LiveKit fires its events from outside Angular, and a signal write is
  enough to schedule a render, so there is no `NgZone.run()` anywhere.
- The call starts when the teller **joins**, not when their camera arrives. A teller
  with video off is still on the call.
