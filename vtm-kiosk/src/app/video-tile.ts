import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  viewChild,
} from '@angular/core';
import type { LocalTrackPublication, RemoteTrack } from 'livekit-client';

/**
 * Binds a LiveKit track to a `<video>` element.
 *
 * `attach()` and `detach()` are the whole of it. The element has to exist before the track can
 * be bound to it, which is why this is a component rather than a directive on a signal.
 */
@Component({
  selector: 'vtm-video-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <video #el [muted]="muted()" autoplay playsinline></video>
  `,
  styles: `
    :host { display: block; }
    video {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
      background: #000;
    }
  `,
})
export class VideoTile {
  readonly track = input<RemoteTrack | LocalTrackPublication | undefined>();

  /** Always true for the local preview, or the kiosk echoes itself. */
  readonly muted = input(false);

  /** Mirror the local preview. A camera feed of yourself that is not mirrored feels wrong. */
  readonly mirror = input(false);

  private readonly el = viewChild.required<ElementRef<HTMLVideoElement>>('el');

  constructor() {
    effect((onCleanup) => {
      const t = this.track();
      const node = this.el().nativeElement;

      node.style.transform = this.mirror() ? 'scaleX(-1)' : '';

      if (!t) return;

      const media = 'videoTrack' in t ? t.videoTrack : t;
      if (!media) return;

      media.attach(node);
      onCleanup(() => media.detach(node));
    });
  }
}
