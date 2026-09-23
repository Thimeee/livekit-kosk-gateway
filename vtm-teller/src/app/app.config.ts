import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // LiveKit and SignalR both fire from outside Angular. With zoneless change
    // detection a signal write schedules a render by itself, so no NgZone is needed.
    provideZonelessChangeDetection(),

    provideRouter(routes),
  ],
};
