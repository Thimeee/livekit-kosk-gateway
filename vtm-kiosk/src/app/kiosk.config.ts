/**
 * Kiosk deployment settings.
 *
 * The device secret is deliberately NOT here. It is read at runtime from `public/kiosk.secret`,
 * which is gitignored, so enrolling a kiosk cannot put a live credential into a commit — which
 * is exactly what happened once (PROJECT.md D-026).
 *
 * A real kiosk does not use a file at all: it receives its secret once at enrolment and stores it
 * where the machine can protect it, under DPAPI or the TPM. See D-021.
 */
export const KIOSK_CONFIG = {
  apiBaseUrl: 'https://livekit.nipunmcs.biz',

  /** ws://, not http:// — that one is for the management API. */
  liveKitUrl: 'wss://monapisam.nipunmcs.biz',

  kioskId: 'K-01',

  /** Where the enrolment secret is read from, relative to the served app. */
  secretUrl: 'kiosk.secret',

  /**
   * Show the customer a badge while the teller is viewing their screen.
   *
   * Off by request. Worth knowing before leaving it that way: telling someone their screen is
   * being watched is usually a compliance requirement in a bank rather than a courtesy, so a
   * reviewer may well ask for it. It is one flag, deliberately, so that is a one-line change.
   */
  showScreenShareIndicator: false,
} as const;

/**
 * Reads the enrolment secret. Returns empty when the kiosk has not been enrolled, which the
 * boot sequence turns into a visible "not ready" screen rather than a broken Start button.
 */
export async function readDeviceSecret(): Promise<string> {
  try {
    const res = await fetch(KIOSK_CONFIG.secretUrl, { cache: 'no-store' });
    if (!res.ok) return '';

    const text = (await res.text()).trim();
    // A missing file on a dev server often returns index.html with a 200.
    return text.startsWith('<') ? '' : text;
  } catch {
    return '';
  }
}
