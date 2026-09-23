import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { DialogHostComponent, ToastContainerComponent } from '@thimi/muk-kit';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ToastContainerComponent, DialogHostComponent],
  template: `
    <router-outlet />

    <!-- Both are required, not decorative: ToastService and DialogService push onto
         signals only these components read. With no host mounted, confirm() resolves
         nothing and no toast ever appears - silently. -->
    <muk-toast-container />
    <muk-dialog-host />
  `,
})
export class App {}
