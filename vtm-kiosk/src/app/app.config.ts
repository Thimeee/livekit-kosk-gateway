import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // LiveKit fires its events from outside Angular. With zoneless change detection a
    // signal write is enough to schedule a render, so no NgZone.run() is needed anywhere.
    provideZonelessChangeDetection(),
  ]
};
