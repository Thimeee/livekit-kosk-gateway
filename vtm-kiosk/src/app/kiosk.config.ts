/**
 * Kiosk settings, resolved at runtime rather than compiled in.
 *
 * One Angular build serves every kiosk in the estate. A `kioskId` baked into the bundle would
 * mean a build per machine, which is not a thing anyone can operate.
 *
 * Three sources, first one wins:
 *
 * 1. **`window.__vtmKiosk`** — injected by the WPF shell before the page runs, from the shell's
 *    own `kiosk-shell.json`. This is how a real kiosk is configured: the identity of the device
 *    lives on the device.
 * 2. **`kiosk-config.json`** beside the page — for running in a plain browser, where there is no
 *    shell to inject anything.
 * 3. **The defaults below** — a developer's machine, nothing configured.
 */

export interface KioskConfig {
  apiBaseUrl: string;

  /** ws:// or wss://, not http:// — that one is for the management API. */
  liveKitUrl: string;

  /** Which kiosk this machine is. The one value that genuinely differs per device. */
  kioskId: string;

  /**
   * Show the customer a badge while the teller is viewing their screen.
   *
   * Off by request. Worth knowing before leaving it that way: telling someone their screen is
   * being watched is usually a compliance requirement in a bank rather than a courtesy.
   */
  showScreenShareIndicator: boolean;
}

/** What the shell may set. The secret is separate because it is a credential, not a setting. */
interface InjectedConfig extends Partial<KioskConfig> {
  deviceSecret?: string;
}

declare global {
  interface Window {
    __vtmKiosk?: InjectedConfig;
  }
}

const DEFAULTS: KioskConfig = {
  apiBaseUrl: 'http://localhost:5065',
  liveKitUrl: 'ws://localhost:7880',
  kioskId: 'K-01',
  showScreenShareIndicator: false,
};

/** Where a plain browser looks, when there is no shell. */
const CONFIG_URL = 'kiosk-config.json';
const SECRET_URL = 'kiosk.secret';

let resolved: KioskConfig = DEFAULTS;
let secret = '';

/**
 * Reads the configuration. Call once, at boot, before anything uses it.
 *
 * Never throws: a kiosk that cannot read its settings still has to start and show why, and a
 * wrong `apiBaseUrl` announces itself within a second anyway.
 */
export async function loadKioskConfig(): Promise<KioskConfig> {
  const injected = window.__vtmKiosk;

  if (injected) {
    // Under the shell. Its config file is the single source of truth for this machine.
    resolved = { ...DEFAULTS, ...strip(injected) };
    secret = injected.deviceSecret?.trim() ?? '';
    return resolved;
  }

  resolved = { ...DEFAULTS, ...(await fetchJson<Partial<KioskConfig>>(CONFIG_URL) ?? {}) };
  secret = await fetchSecret();

  return resolved;
}

/** The settings, after {@link loadKioskConfig}. Returns the defaults before that. */
export function kioskConfig(): KioskConfig {
  return resolved;
}

/**
 * The enrolment secret. Empty when this kiosk has not been enrolled, which the boot sequence
 * turns into a visible "not ready" screen rather than a Start button that cannot work.
 */
export function deviceSecret(): string {
  return secret;
}

/** `deviceSecret` is a credential and must not end up in the settings object. */
function strip(injected: InjectedConfig): Partial<KioskConfig> {
  const { deviceSecret: _ignored, ...rest } = injected;
  return rest;
}

async function fetchJson<T>(url: string): Promise<T | undefined> {
  try {
    const res = await fetch(url, { cache: 'no-store' });
    if (!res.ok) return undefined;

    // A dev server answers a missing file with index.html and a 200.
    const text = (await res.text()).trim();
    if (!text.startsWith('{')) return undefined;

    return JSON.parse(text) as T;
  } catch {
    return undefined;
  }
}

async function fetchSecret(): Promise<string> {
  try {
    const res = await fetch(SECRET_URL, { cache: 'no-store' });
    if (!res.ok) return '';

    const text = (await res.text()).trim();
    return text.startsWith('<') ? '' : text;
  } catch {
    return '';
  }
}
