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

## Configuration

`kiosk-shell.json`, beside the executable. Missing or malformed means the defaults — a kiosk must
still start and say what is wrong on screen, rather than not start at all.

| Key | Default | |
|---|---|---|
| `kioskUrl` | `http://localhost:4201` | The page to host. Navigation anywhere else is blocked. |
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
| Kiosk page loaded and device-authenticated | ✅ |
| Camera inside WebView2, **no prompt answered** | **1280x720** |
| Teller audio playing in the shell | **RMS 0.304** |
| **Teller viewed the kiosk screen with nothing clicked in the shell** | **960x540** |
| Still one control only | `["I'm finished"]` |
| Back to idle after the teller ended it | ✅ |
