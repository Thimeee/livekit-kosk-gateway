import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonComponent, CardComponent, InputComponent } from '@thimi/muk-kit';
import { TellerService } from './teller.service';

@Component({
  selector: 'vtm-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, CardComponent, InputComponent, ButtonComponent],
  template: `
    <div class="wrap">
      <muk-card title="VTM Teller" subtitle="Sign in to take customers" variant="elevated">
        <form (ngSubmit)="submit()">
          <muk-input
            label="Teller id"
            name="username"
            [(ngModel)]="username"
            [disabled]="busy()"
            autocomplete="username"
          />

          <muk-input
            label="Password"
            type="password"
            name="password"
            [(ngModel)]="password"
            [disabled]="busy()"
            autocomplete="current-password"
          />

          <!-- A real submit button, so Enter works without wiring a keydown handler. -->
          <muk-button
            variant="primary"
            type="submit"
            [block]="true"
            [loading]="busy()"
            loadingText="Signing in…"
          >
            Sign in
          </muk-button>
        </form>
      </muk-card>
    </div>
  `,
  styles: `
    .wrap {
      min-height: 100dvh;
      display: grid;
      place-items: center;
      padding: 1.5rem;
    }
    muk-card { width: min(26rem, 100%); }
    form { display: flex; flex-direction: column; gap: 1rem; }
  `,
})
export class Login {
  private readonly teller = inject(TellerService);
  private readonly router = inject(Router);

  protected username = '';
  protected password = '';
  protected readonly busy = signal(false);

  protected async submit(): Promise<void> {
    if (this.busy() || !this.username || !this.password) return;

    this.busy.set(true);
    const ok = await this.teller.signIn(this.username, this.password);
    this.busy.set(false);

    if (ok) await this.router.navigate(['/console']);
  }
}
