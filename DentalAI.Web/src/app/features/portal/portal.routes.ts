import { Routes } from '@angular/router';
import { portalGuard } from './portal-context';
import { PortalShellComponent } from './portal-shell.component';

/** /p/:slug — a clinic's patient portal. Outside the staff app shell and its guards. */
export const PORTAL_ROUTES: Routes = [
  {
    path: '',
    component: PortalShellComponent,
    children: [
      {
        path: 'sign-in',
        title: 'Sign in',
        loadComponent: () => import('./portal-sign-in.component').then((m) => m.PortalSignInComponent)
      },
      {
        path: '',
        canActivate: [portalGuard],
        children: [
          {
            path: '',
            pathMatch: 'full',
            title: 'My appointments',
            loadComponent: () => import('./portal-home.component').then((m) => m.PortalHomeComponent)
          },
          {
            path: 'book',
            title: 'Book a visit',
            loadComponent: () => import('./portal-book.component').then((m) => m.PortalBookComponent)
          },
          {
            path: 'appointments/:id/reschedule',
            title: 'Change appointment',
            loadComponent: () => import('./portal-book.component').then((m) => m.PortalBookComponent)
          },
          {
            path: 'assistant',
            title: 'Ask us',
            loadComponent: () => import('./portal-assistant.component').then((m) => m.PortalAssistantComponent)
          },
          {
            path: 'invoices',
            title: 'Invoices',
            loadComponent: () => import('./portal-invoices.component').then((m) => m.PortalInvoicesComponent)
          },
          {
            path: 'invoices/:id',
            title: 'Invoice',
            loadComponent: () => import('./portal-invoice.component').then((m) => m.PortalInvoiceComponent)
          },
          {
            path: 'profile',
            title: 'My details',
            loadComponent: () => import('./portal-profile.component').then((m) => m.PortalProfileComponent)
          }
        ]
      },
      { path: '**', redirectTo: '' }
    ]
  }
];
