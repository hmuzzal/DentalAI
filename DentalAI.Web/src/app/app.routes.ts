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
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        title: 'Dashboard',
        loadComponent: () => import('./features/dashboard/dashboard-page.component').then((m) => m.DashboardPageComponent)
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
        loadComponent: () => import('./features/doctors/doctors-page.component').then((m) => m.DoctorsPageComponent)
      },
      {
        path: 'services',
        title: 'Services',
        loadComponent: () => import('./features/services/services-page.component').then((m) => m.ServicesPageComponent)
      },
      {
        path: 'patients',
        title: 'Patients',
        loadComponent: () => import('./features/patients/patients-page.component').then((m) => m.PatientsPageComponent)
      },
      {
        path: 'appointments',
        title: 'Appointments',
        loadComponent: () => import('./features/appointments/appointments-page.component').then((m) => m.AppointmentsPageComponent)
      },
      {
        path: 'calendar',
        title: 'Calendar',
        loadComponent: () => import('./features/calendar/calendar-page.component').then((m) => m.CalendarPageComponent)
      },
      {
        path: 'doctor-schedules',
        title: 'Doctor schedules',
        loadComponent: () => import('./features/doctor-schedules/doctor-schedules-page.component').then((m) => m.DoctorSchedulesPageComponent)
      },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: '/dashboard' }
];
