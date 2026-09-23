# VTM Teller console

Sign in, watch the queue, take a customer, run the call.

Built with [MUK UI Kit](https://www.npmjs.com/package/@thimi/muk-kit) — the same kit
the bank's other screens use.

## Running it

The API and the LiveKit server must both be up.

```bash
npm start        # http://localhost:4200
```

Demo accounts (seeded when `DemoData:Enabled` is on):

| Who | Password | Role |
|---|---|---|
| `teller1` | `Demo!2026` | Nimal Perera, teller |
| `teller2` | `Demo!2026` | Kamala Silva, teller |
| `super1`  | `Demo!2026` | Ranjith Fernando, supervisor |

## Notes

- **The ring arrives over SignalR**, not by polling. The badge in the header shows
  whether that feed is up: a teller whose connection dropped stops being rung and
  would otherwise never know.
- **Losing the accept race is normal.** Two tellers pressing Accept on the same
  customer is expected; the loser gets a warning toast and the row disappears.
- **The kiosk screen takes the large tile** when it is being shared. That is what
  the teller is being asked to look at; the customer's face moves to the side.
- Zoneless. LiveKit and SignalR both fire from outside Angular, and a signal write
  schedules a render by itself.
- `muk-toast-container` and `muk-dialog-host` are mounted in `app.ts`. Without them
  `toast.*` and `dialog.confirm()` do nothing at all — silently.
