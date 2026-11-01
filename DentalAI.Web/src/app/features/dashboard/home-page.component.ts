import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CLINIC_STAFF } from '../../core/models/auth.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { PlatformOverviewPageComponent } from '../platform/platform-overview-page.component';
import { DashboardPageComponent } from './dashboard-page.component';

/**
 * /dashboard: clinic staff get their clinic's dashboard; platform admins get the platform overview. The clinic
 * dashboard is never created for platform admins, so it never calls the clinic endpoints they can't use.
 */
@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [DashboardPageComponent, PlatformOverviewPageComponent],
  template: `
    @if (isClinicStaff()) {
      <app-dashboard-page />
    } @else if (authState.user()) {
      <app-platform-overview-page />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HomePageComponent {
  readonly authState = inject(AuthStateService);
  readonly isClinicStaff = computed(() => this.authState.hasAnyRole(CLINIC_STAFF));
}
