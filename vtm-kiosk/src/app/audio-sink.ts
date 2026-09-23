import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  viewChild,
} from '@angular/core';
import type { RemoteTrack } from 'livekit-client';

/**
 * Plays a remote audio track through an element that is really in the document.
 *
 * `track.attach()` with no argument creates an element and never appends it. The SDK calls
 * play() on it and it usually works, but a detached element cannot be reached to set volume
 * or an output device, Safari will not play one at all, and Chrome is free to suspend it.
 * Audio silently not working is the worst failure this app has, so it gets a real element.
 *
 * Hidden by size rather than `display: none` - a display:none media element is a thing some
 * browsers feel entitled to stop.
 */
@Component({
  selector: 'vtm-audio-sink',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<audio #el autoplay></audio>`,
  styles: `
    :host {
      position: absolute;
      width: 0;
      height: 0;
      overflow: hidden;
      pointer-events: none;
    }
  `,
})
export class AudioSink {
  readonly track = input<RemoteTrack | undefined>();

  private readonly el = viewChild.required<ElementRef<HTMLAudioElement>>('el');

  constructor() {
    effect(onCleanup => {
      const t = this.track();
      if (!t) return;

      const node = this.el().nativeElement;
      t.attach(node);
      onCleanup(() => t.detach(node));
    });
  }
}
