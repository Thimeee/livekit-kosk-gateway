import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
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

  /** First letter of the teller's name, for the camera-off placeholder. */
  /**
   * Comes from the settings, which are not read until boot - so this follows the service
   * rather than being fixed when the component is created.
   */
  protected readonly showsSharing = this.kiosk.showScreenShareIndicator;

  protected readonly initial = computed(() =>
    (this.kiosk.tellerName().replace(/^teller-/, '').trim()[0] ?? '?').toUpperCase(),
  );

  ngOnInit(): void {
    // A kiosk authenticates itself on power-up. Nobody is here to log it in.
    void this.kiosk.boot();
  }
}
