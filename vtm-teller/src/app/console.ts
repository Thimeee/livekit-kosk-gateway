import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  AlertComponent,
  AvatarComponent,
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  DialogService,
  EmptyStateComponent,
  PageHeaderComponent,
  SwitchComponent,
} from '@thimi/muk-kit';
import { TellerService, type QueueEntry } from './teller.service';
import { VideoTile } from './video-tile';
import { AudioSink } from './audio-sink';

@Component({
  selector: 'vtm-console',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PageHeaderComponent,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    AvatarComponent,
    AlertComponent,
    EmptyStateComponent,
    SwitchComponent,
    VideoTile,
    AudioSink,
  ],
  templateUrl: './console.html',
  styleUrl: './console.scss',
})
export class Console {
  protected readonly teller = inject(TellerService);
  private readonly dialog = inject(DialogService);
  private readonly router = inject(Router);

  protected readonly initial = computed(() =>
    (this.teller.customerName().replace(/^kiosk-/, '').trim()[0] ?? '?').toUpperCase(),
  );

  /** A supervisor covering several branches needs to know which one a customer is at. */
  protected readonly showsBranch = computed(() => this.teller.me()?.role !== 'teller');

  protected wait(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m ? `${m}m ${s}s` : `${s}s`;
  }

  /** Over two minutes waiting is worth showing as a problem, not a number. */
  protected waitVariant(seconds: number): 'success' | 'warning' | 'danger' {
    if (seconds > 180) return 'danger';
    if (seconds > 60) return 'warning';
    return 'success';
  }

  protected accept(entry: QueueEntry): void {
    void this.teller.accept(entry);
  }

  protected async end(): Promise<void> {
    const ok = await this.dialog.confirm({
      title: 'End this session?',
      message: 'The customer will be disconnected and the session closed.',
      confirmText: 'End session',
      cancelText: 'Stay',
      variant: 'danger',
    });

    if (ok) await this.teller.leaveCall(true);
  }

  protected async signOut(): Promise<void> {
    if (this.teller.inCall()) {
      const ok = await this.dialog.confirm({
        title: 'Sign out during a call?',
        message: 'You will leave the call. The customer stays in the queue for someone else.',
        confirmText: 'Sign out',
        cancelText: 'Stay',
        variant: 'danger',
      });

      if (!ok) return;
    }

    await this.teller.signOut();
    await this.router.navigate(['/login']);
  }
}
