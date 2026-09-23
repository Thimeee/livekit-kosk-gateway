# VTM Kiosk Shell

The WPF application a kiosk actually runs. It is a full-screen window hosting the kiosk page in
WebView2, and nothing else.

## Why it exists

The page does the work. This is here for the things a browser tab cannot do — and the first of
them is already load-bearing.

**Screen sharing.** `getDisplayMedia()` needs a user gesture and opens a source picker. On a kiosk
there is nobody to click it, so the teller's *View their screen* command sits waiting and times
out. Passing `--auto-select-desktop-capture-source` at launch is what makes it work unattended,
and only the host process can pass it.

Measured, against the browser installed on this machine:

| Launched | `getDisplayMedia()` with nothing clicked |
|---|---|
| An ordinary window | picker opens and waits |
| With the flag | `{"ok":true,"label":"screen:0:0"}` |
| With `="Screen 1"` | picker — **the source name must match exactly** |

**Camera and microphone** are granted here rather than prompted for. Nobody is standing at a kiosk
to answer a permission dialog, and those two devices are the point of the machine. Everything else
is refused, and any origin other than the configured one is refused outright.

Later this is also where the card reader and the printer go, since a browser can reach neither.

## Where the page comes from

Two answers, and the shell does either.

| | **Bundled** (`webRoot` set) | **Served** (`kioskUrl` only) |
|---|---|---|
| Web server for the page | **none** | one, reachable from every kiosk |
| Updating the page | update every kiosk | one place, all kiosks follow |
| Page server down | **kiosk still works** | kiosk shows nothing |
| Installing | copy one folder | install, then point at the URL |

**Bundled is the right default for a branch**, and it is the demo setup: the built Angular output
sits in `webroot/` next to the executable and WebView2 serves it under `https://kiosk.vtm/`.

That virtual host is not a network name and resolves nowhere — it exists so the page has a **real
origin**. `file://` would not do: a file:// page has an opaque origin, so its calls to the API
arrive cross-origin with a null `Origin` header and no CORS setting can allow them.

The API must allow whichever origin is in use. `https://kiosk.vtm` is in the development CORS list
for exactly this reason.

```bash
# put the built page inside the shell
cd ../vtm-kiosk && npm run build
cp -r dist/vtm-kiosk/browser ../vtm-kiosk-shell/bin/Release/net10.0-windows/webroot
```

## One build, many kiosks

A `kioskId` compiled into the Angular bundle would mean a build per machine. So the page resolves
its settings at runtime, from the first of these that answers:

1. **`window.__vtmKiosk`** — injected by this shell before the page runs, from `kiosk-shell.json`
2. **`kiosk-config.json`** beside the page — for a plain browser, where there is no shell
3. **The page's own defaults** — a developer's machine

The identity of a device lives on the device, in one file, and the same `webroot/` serves every
kiosk in the estate. Proved by pointing two configurations at one bundle: with `kioskId: "K-02"`
the teller's queue showed **K-02 / Kandy City Lobby 2**, while the bundle's own default was still
`K-01`.

**The secret travels the same way**, and deliberately does not travel with the page: Angular
copies `public/` into `dist/`, so a `kiosk.secret` left there for browser development would
otherwise be bundled into every kiosk install. The publish step excludes it.

Today the secret sits in `kiosk-shell.json` because this is a demo. A real kiosk keeps it where the
machine can protect it — DPAPI or the TPM ([D-021](../docs/PROJECT.md#d-021)). When that changes,
only the shell changes; the page never knows the difference.

## Building a kiosk

```bash
dotnet publish -c Release -o publish
```

That builds the Angular page, bundles it into `publish/webroot/`, and leaves a folder you can copy
to a machine. `/p:BuildKioskPage=false` skips the page build; a plain `dotnet build` never runs it,
so development does not sit through an `ng build` it did not ask for.

## Configuration

`kiosk-shell.json`, beside the executable. Missing or malformed means the defaults — a kiosk must
still start and say what is wrong on screen, rather than not start at all.

| Key | Default | |
|---|---|---|
| `kioskId` | *(empty)* | **Which kiosk this machine is.** The one setting that differs per device. |
| `apiBaseUrl` | *(empty)* | Empty leaves the page's own default. |
| `liveKitUrl` | *(empty)* | Empty leaves the page's own default. |
| `deviceSecret` | *(empty)* | The enrolment secret. Injected, never bundled. |
| `showScreenShareIndicator` | `false` | Tell the customer when the teller is viewing their screen. Usually a compliance call. |
| `webRoot` | *(empty)* | Serve the page from this folder instead of fetching a URL. Relative to the executable. |
| `virtualHost` | `kiosk.vtm` | The origin a bundled page is served under. Must not resolve on the network. |
| `kioskUrl` | `http://localhost:4201` | Used only when `webRoot` is empty. Navigation anywhere else is blocked. |
| `captureSource` | `Entire screen` | Must match a capture source name **exactly**. |
| `userDataFolder` | `%LOCALAPPDATA%\VtmKiosk\WebView2` | Deliberately not the shared default, so the granted camera permission and this kiosk's state stay its own. |
| `locked` | `false` | `true` on a real kiosk — removes the way out. |
| `devTools` | `false` | |
| `fakeMedia` | `false` | For a machine with no camera. Never on a real kiosk. |
| `remoteDebuggingPort` | `0` | Non-zero opens a CDP port for testing. **Never set this on a real kiosk.** |

A real branch kiosk:

```json
{
  "kioskUrl": "https://kiosk.internal.example",
  "captureSource": "Entire screen",
  "locked": true,
  "devTools": false,
  "fakeMedia": false,
  "remoteDebuggingPort": 0
}
```

While `locked` is false, **Ctrl+Shift+Q** closes the window. That is for whoever installs the
machine; with `locked: true` there is no key combination at all.

## Running it

```bash
dotnet run                      # or bin/Release/net10.0-windows/VtmKiosk.exe
```

The kiosk page must be served and enrolled first — see [../vtm-kiosk/README.md](../vtm-kiosk/README.md).

## Requirements

- **WebView2 Runtime.** Present on current Windows 11; otherwise install the Evergreen runtime.
- **.NET 10** with the Windows Desktop workload.

## Verified

Driven end to end through the shell's own CDP port, so what was measured is the real WebView2 with
the real launch arguments — not a browser a test started for itself:

| | |
|---|---|
| Page served from inside the shell, **nothing serving it on the network** | `https://kiosk.vtm/index.html` |
| Kiosk page loaded and device-authenticated | ✅ |
| Camera inside WebView2, **no prompt answered** | **1280x720** |
| Teller audio playing in the shell | **RMS 0.304** |
| **Teller viewed the kiosk screen with nothing clicked in the shell** | **960x540** |
| Still one control only | `["I'm finished"]` |
| Back to idle after the teller ended it | ✅ |
