import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { HostBridge } from './host-bridge';
import { KioskService } from './kiosk.service';
import { VideoTile } from './video-tile';
import { AudioSink } from './audio-sink';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [VideoTile, AudioSink],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly kiosk = inject(KioskService);

  /**
   * Injected for its side effect: it wires the page to a host application when there is one.
   * `hosted` is what the template uses to drop its own chrome.
   */
  protected readonly host = inject(HostBridge);

  /** First letter of the teller's name, for the camera-off placeholder. */
  /**
   * Comes from the settings, which are not read until boot - so this follows the service
   * rather than being fixed when the component is created.
   */
  protected readonly showsSharing = this.kiosk.showScreenShareIndicator;

  /** One line, already worded for a customer glancing at a small window. */
  protected readonly embedMessage = computed(() => {
    if (this.kiosk.reconnecting()) return 'Reconnecting…';

    switch (this.kiosk.screen()) {
      case 'booting': return 'Starting…';
      case 'waiting': return this.kiosk.statusText() || 'Connecting you to a teller…';
      case 'error': return this.kiosk.errorText() || 'This kiosk is not ready.';
      case 'incall': return 'The teller has their camera off.';
      default: return '';
    }
  });

  protected readonly initial = computed(() =>
    (this.kiosk.tellerName().replace(/^teller-/, '').trim()[0] ?? '?').toUpperCase(),
  );

  ngOnInit(): void {
    // A kiosk authenticates itself on power-up. Nobody is here to log it in.
    void this.kiosk.boot();
  }
}
