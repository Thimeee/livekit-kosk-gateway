/**
 * Notices that a call has gone quiet, without waiting for the SDK to agree.
 *
 * The SDK takes about 15 seconds to decide a connection is dead (PROJECT.md K-14). For someone
 * standing at a kiosk mid-transaction that is 15 seconds of frozen picture and no explanation,
 * which is the worst thing this screen can do.
 *
 * Two signals, because neither covers the other:
 *
 * - **`navigator.onLine`** — measured at **2ms** when the network is cut. Instant, and it is the
 *   commonest kiosk failure there is: someone unplugs it, or the wifi drops. It says nothing
 *   about the far end.
 * - **A media watchdog** — if frames stop arriving, the link is down however healthy this
 *   machine's own network looks. Covers the server going away, or the path to it breaking.
 *
 * Deliberately not used: `ConnectionQuality`. Its reports arrive over the signal connection, so
 * a cut produces no report at all - measured, see D-029.
 */

export interface LinkWatchdogOptions {
  /**
   * How long media may stand still before the link is called stalled.
   *
   * Long enough not to fire on an ordinary decode hiccup, short enough to beat the SDK.
   */
  stallAfterMs?: number;

  /** How often to look. */
  pollMs?: number;
}

export class LinkWatchdog {
  private timer?: ReturnType<typeof setInterval>;
  private probe?: () => number | undefined;
  private lastValue?: number;
  private lastMovedAt = 0;
  private stalled = false;

  private readonly stallAfterMs: number;
  private readonly pollMs: number;

  private readonly onOffline = () => this.report(true);
  private readonly onOnline = () => {
    // Coming back does not mean the call recovered - the SDK still has to reconnect. Let the
    // media probe decide, and reset its clock so it does not fire on the gap we just had.
    this.lastMovedAt = Date.now();
    this.report(!navigator.onLine);
  };

  constructor(
    private readonly onChange: (stalled: boolean) => void,
    options: LinkWatchdogOptions = {},
  ) {
    this.stallAfterMs = options.stallAfterMs ?? 4000;
    this.pollMs = options.pollMs ?? 1000;
  }

  /**
   * @param probe returns something that advances while media is flowing - a media element's
   * `currentTime` is the obvious one. Return `undefined` when there is nothing to watch, which
   * suspends the media half without affecting the network half.
   */
  start(probe: () => number | undefined): void {
    this.stop();

    this.probe = probe;
    this.lastValue = undefined;
    this.lastMovedAt = Date.now();

    window.addEventListener('offline', this.onOffline);
    window.addEventListener('online', this.onOnline);

    // The page may already be offline when a call starts.
    if (!navigator.onLine) this.report(true);

    this.timer = setInterval(() => this.tick(), this.pollMs);
  }

  stop(): void {
    clearInterval(this.timer);
    this.timer = undefined;
    window.removeEventListener('offline', this.onOffline);
    window.removeEventListener('online', this.onOnline);
    this.report(false);
  }

  private tick(): void {
    if (!navigator.onLine) {
      this.report(true);
      return;
    }

    const value = this.probe?.();

    if (value === undefined) {
      // Nothing to watch - a camera that is legitimately off, or a track not yet subscribed.
      // Silence here is not evidence of a broken link, so do not claim one.
      this.lastMovedAt = Date.now();
      this.report(false);
      return;
    }

    if (value !== this.lastValue) {
      this.lastValue = value;
      this.lastMovedAt = Date.now();
      this.report(false);
      return;
    }

    if (Date.now() - this.lastMovedAt >= this.stallAfterMs) this.report(true);
  }

  private report(stalled: boolean): void {
    if (stalled === this.stalled) return;

    this.stalled = stalled;
    this.onChange(stalled);
  }
}
