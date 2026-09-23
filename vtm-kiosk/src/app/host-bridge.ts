import { Injectable, effect, inject } from '@angular/core';
import { KioskService } from './kiosk.service';

/**
 * Lets a host application drive the call, and tells it what the call is doing.
 *
 * On a small kiosk screen the banking application owns the display. It shows the teller in a
 * small window at the side, and every control — *talk to a teller*, *I'm finished* — stays in
 * the host's own UI where the rest of the buttons are. So this page stops being an application
 * and becomes a video surface the host operates.
 *
 * The transport is WebView2's own host/page channel. It exists only inside a WebView2 host; in
 * an ordinary browser none of this runs and the page behaves as it always did.
 */

/** What the host may ask for. */
type HostCommand =
  | { type: 'start' }
  | { type: 'end' }
  | { type: 'enableAudio' };

/** What the host is told. One message per change, never a stream. */
export interface HostState {
  type: 'state';
  /** `booting` and `error` matter to a host too: it must not offer a call the kiosk cannot make. */
  screen: 'booting' | 'idle' | 'waiting' | 'incall' | 'error';
  /** Already worded for a person to read. */
  status: string;
  error: string;
  tellerName: string;
  reconnecting: boolean;
  /** True while the browser is refusing to play audio until someone clicks. */
  needsAudioGesture: boolean;
}

interface WebView2Bridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2Bridge };
  }
}

@Injectable({ providedIn: 'root' })
export class HostBridge {
  private readonly kiosk = inject(KioskService);
  private readonly bridge = window.chrome?.webview;

  /** True when a host is driving. The page hides its own controls in that case. */
  readonly hosted = !!this.bridge;

  constructor() {
    if (!this.bridge) return;

    this.bridge.addEventListener('message', event => void this.onCommand(event.data));

    // Report every change, including the first. A host that starts late would otherwise sit
    // with no idea whether the kiosk is ready.
    effect(() => this.publish());
  }

  private async onCommand(raw: unknown): Promise<void> {
    const command = this.parse(raw);
    if (!command) return;

    switch (command.type) {
      case 'start':
        await this.kiosk.startSession();
        break;

      case 'end':
        await this.kiosk.endSession();
        break;

      case 'enableAudio':
        // Must come from a real gesture on the host's side, forwarded here.
        await this.kiosk.enableAudio();
        break;
    }
  }

  /** WebView2 delivers a string or an object depending on how the host posted it. */
  private parse(raw: unknown): HostCommand | undefined {
    try {
      const value = typeof raw === 'string' ? JSON.parse(raw) : raw;
      return typeof value === 'object' && value !== null && 'type' in value
        ? (value as HostCommand)
        : undefined;
    } catch {
      return undefined;
    }
  }

  private publish(): void {
    const state: HostState = {
      type: 'state',
      screen: this.kiosk.screen(),
      status: this.kiosk.statusText(),
      error: this.kiosk.errorText(),
      tellerName: this.kiosk.tellerName(),
      reconnecting: this.kiosk.reconnecting(),
      needsAudioGesture: this.kiosk.needsAudioGesture(),
    };

    this.bridge?.postMessage(JSON.stringify(state));
  }
}
