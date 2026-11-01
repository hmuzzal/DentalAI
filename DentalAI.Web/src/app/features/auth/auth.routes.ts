import { Routes } from '@angular/router';
import { guestGuard } from '../../core/guards/guest.guard';

export const AUTH_ROUTES: Routes = [
  {
    path: 'login',
    title: 'Sign in',
    canActivate: [guestGuard],
    loadComponent: () => import('./login/login-page.component').then((m) => m.LoginPageComponent)
  },
  {
    // Public: the link in an invite. Not guest-guarded, so it also works if someone is signed in on this browser.
    path: 'accept-invite',
    title: 'Set your password',
    loadComponent: () => import('./accept-invite/accept-invite-page.component').then((m) => m.AcceptInvitePageComponent)
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' }
];
