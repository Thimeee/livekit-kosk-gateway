import { Routes } from '@angular/router';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { TellerService } from './teller.service';

/** A console with no signed-in teller has nothing to show and no token to fetch with. */
const signedIn = () => inject(TellerService).signedIn() || inject(Router).createUrlTree(['/login']);

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', loadComponent: () => import('./login').then(m => m.Login) },
  {
    path: 'console',
    canActivate: [signedIn],
    loadComponent: () => import('./console').then(m => m.Console),
  },
  { path: '**', redirectTo: 'login' },
];
