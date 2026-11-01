import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { PlatformOverview } from '../../core/models/platform.models';
import { PlatformService } from '../../core/services/platform.service';
import { timeZoneLabel } from '../../core/time-zone';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

const EMPTY: PlatformOverview = {
  clinics: 0, activeUsers: 0, doctors: 0, patients: 0, appointmentsLast30Days: 0, upcomingAppointments: 0, clinicUsage: []
};

/**
 * The platform admin's dashboard: how each clinic uses DentalAI, as counts. Patient and appointment details are
 * deliberately not available to platform admins (HIPAA minimum necessary).
 */
@Component({
  selector: 'app-platform-overview-page',
  standalone: true,
  imports: [DatePipe, DecimalPipe, RouterLink, NzAlertModule, NzCardModule, NzTableModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Platform overview" eyebrow="Platform"
                       subtitle="Usage across all clinics. Counts only: clinic data stays with each clinic." />

      <app-page-state [status]="overview.status()" (retry)="overview.reload()" [rows]="2"
                      errorText="We could not load the platform overview.">
        <div class="kpis">
          @for (tile of kpis(); track tile.label) {
            <div class="kpi">
              <span class="kpi__label">{{ tile.label }}</span>
              <strong class="kpi__value">{{ tile.value | number }}</strong>
            </div>
          }
        </div>
      </app-page-state>

      <nz-card nzTitle="Clinics" [nzExtra]="manage">
        <ng-template #manage><a routerLink="/clinics">Manage clinics</a></ng-template>
        <app-page-state [status]="overview.status()" [empty]="!overview.value().clinicUsage.length"
                        (retry)="overview.reload()" emptyText="No clinics yet">
          <nz-table [nzData]="overview.value().clinicUsage" [nzPageSize]="25" [nzHideOnSinglePage]="true" nzSize="middle">
            <thead>
              <tr>
                <th>Clinic</th>
                <th>Time zone</th>
                <th nzAlign="right">Staff</th>
                <th nzAlign="right">Doctors</th>
                <th nzAlign="right">Patients</th>
                <th nzAlign="right">Appointments, 30 days</th>
                <th nzAlign="right">Upcoming</th>
                <th>Joined</th>
              </tr>
            </thead>
            <tbody>
              @for (clinic of overview.value().clinicUsage; track clinic.clinicId) {
                <tr>
                  <td><strong>{{ clinic.name }}</strong> <span class="muted">{{ clinic.country }}</span></td>
                  <td>{{ zone(clinic.timeZone) }}</td>
                  <td nzAlign="right" class="num">{{ clinic.activeUsers | number }}</td>
                  <td nzAlign="right" class="num">{{ clinic.doctors | number }}</td>
                  <td nzAlign="right" class="num">{{ clinic.patients | number }}</td>
                  <td nzAlign="right" class="num">{{ clinic.appointmentsLast30Days | number }}</td>
                  <td nzAlign="right" class="num">{{ clinic.upcomingAppointments | number }}</td>
                  <td class="num nowrap">{{ clinic.createdDate | date: 'MMM d, yyyy' }}</td>
                </tr>
              }
            </tbody>
          </nz-table>
        </app-page-state>
      </nz-card>

      <nz-alert nzType="info" nzShowIcon
                nzMessage="Platform admins can't open a clinic's patients, appointments, doctors or services."
                nzDescription="This keeps patient data with the clinic (HIPAA minimum necessary). Time-limited, audited support access to a clinic is planned." />
    </section>
  `,
  styles: [`
    .kpis { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: var(--da-space-3); }
    .kpi { display: grid; gap: 4px; padding: var(--da-space-4); background: var(--da-surface); border: 1px solid var(--da-border);
      border-radius: var(--da-radius); }
    .kpi__label { font-size: 13px; color: var(--da-ink-2); }
    .kpi__value { font-size: 28px; line-height: 36px; font-weight: 600; color: var(--da-ink); font-variant-numeric: tabular-nums; }
    .muted { color: var(--da-ink-3); font-size: 12px; margin-left: 4px; }
    .num { font-variant-numeric: tabular-nums; }
    .nowrap { white-space: nowrap; }
    @media (max-width: 1200px) { .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlatformOverviewPageComponent {
  private readonly platform = inject(PlatformService);
  readonly overview = remoteData<PlatformOverview>(() => this.platform.getOverview(), EMPTY);

  readonly kpis = computed(() => {
    const o = this.overview.value();
    return [
      { label: 'Clinics', value: o.clinics },
      { label: 'Active staff', value: o.activeUsers },
      { label: 'Doctors', value: o.doctors },
      { label: 'Patients', value: o.patients },
      { label: 'Appointments, 30 days', value: o.appointmentsLast30Days },
      { label: 'Upcoming appointments', value: o.upcomingAppointments }
    ];
  });

  zone(id: string): string {
    return timeZoneLabel(id) ?? id;
  }
}
