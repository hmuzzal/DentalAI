import { CLINIC_STAFF } from './core/models/auth.models';
import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { AppShellComponent } from './layout/app-shell.component';

export const routes: Routes = [
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.AUTH_ROUTES)
  },
  {
    // The printable invoice: outside the app layout so it prints cleanly.
    path: 'invoices/:id/print',
    title: 'Invoice',
    canActivate: [authGuard, roleGuard],
    data: { roles: CLINIC_STAFF },
    loadComponent: () => import('./features/billing/invoice-print-page.component').then((m) => m.InvoicePrintPageComponent)
  },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'invoices',
        title: 'Invoices',
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/billing/invoices-page.component').then((m) => m.InvoicesPageComponent)
      },
      {
        path: 'invoices/:id',
        title: 'Invoice',
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/billing/invoice-page.component').then((m) => m.InvoicePageComponent)
      },
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () => import('./features/dashboard/home-page.component').then((m) => m.HomePageComponent)
      },
      {
        path: 'clinics',
        title: 'Clinics',
        canActivate: [roleGuard],
        data: { roles: ['SuperAdmin'] },
        loadComponent: () => import('./features/clinics/clinics-page.component').then((m) => m.ClinicsPageComponent)
      },
      {
        path: 'doctors',
        title: 'Doctors',
        // Clinic data: clinic staff only (platform admins manage clinics, not their data).
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/doctors/doctors-page.component').then((m) => m.DoctorsPageComponent)
      },
      {
        path: 'services',
        title: 'Services',
        // Clinic data: clinic staff only (platform admins manage clinics, not their data).
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/services/services-page.component').then((m) => m.ServicesPageComponent)
      },
      {
        path: 'patients',
        title: 'Patients',
        // Clinic data: clinic staff only (platform admins manage clinics, not their data).
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/patients/patients-page.component').then((m) => m.PatientsPageComponent)
      },
      {
        path: 'appointments',
        title: 'Appointments',
        // Clinic data: clinic staff only (platform admins manage clinics, not their data).
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/appointments/appointments-page.component').then((m) => m.AppointmentsPageComponent)
      },
      // The calendar is now a view of the Appointments page; keep old links working.
      { path: 'calendar', redirectTo: 'appointments' },
      {
        path: 'settings/billing',
        title: 'Billing settings',
        canActivate: [roleGuard],
        data: { roles: ['ClinicAdmin'] },
        loadComponent: () => import('./features/settings/billing-settings-page.component').then((m) => m.BillingSettingsPageComponent)
      },
      {
        path: 'settings/notifications',
        title: 'Reminders & messages',
        canActivate: [roleGuard],
        data: { roles: ['ClinicAdmin'] },
        loadComponent: () =>
          import('./features/settings/notification-settings-page.component').then((m) => m.NotificationSettingsPageComponent)
      },
      {
        path: 'reports',
        title: 'Reports',
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/reports/reports-page.component').then((m) => m.ReportsPageComponent)
      },
      {
        path: 'messages',
        title: 'Messages',
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/messages/messages-page.component').then((m) => m.MessagesPageComponent)
      },
      {
        path: 'staff',
        title: 'Staff',
        // A clinic's admins manage its staff; receptionists can't, and platform admins use Clinics > Staff.
        canActivate: [roleGuard],
        data: { roles: ['ClinicAdmin'] },
        loadComponent: () => import('./features/staff/staff-page.component').then((m) => m.StaffPageComponent)
      },
      {
        path: 'doctor-schedules',
        title: 'Doctor schedules',
        // Clinic data: clinic staff only (platform admins manage clinics, not their data).
        canActivate: [roleGuard],
        data: { roles: CLINIC_STAFF },
        loadComponent: () => import('./features/doctor-schedules/doctor-schedules-page.component').then((m) => m.DoctorSchedulesPageComponent)
      },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: '/dashboard' }
];
