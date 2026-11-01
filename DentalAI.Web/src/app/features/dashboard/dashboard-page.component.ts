import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { addDays, format, parseISO, subDays } from 'date-fns';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { forkJoin, map } from 'rxjs';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { Appointment } from '../../core/models/appointment.models';
import { DashboardStats, SchedulingDashboard } from '../../core/models/dashboard.models';
import { AppointmentsService } from '../../core/services/appointments.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { AuthStateService } from '../../core/services/auth-state.service';
import { BookingService, onAppointmentsChanged } from '../../core/services/booking.service';
import { dateKeyInZone, effectiveTimeZone, formatAppointmentTime, formatInZone } from '../../core/time-zone';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { formatUsd, RevenueBar, RevenueChartComponent } from './revenue-chart.component';

interface DashboardData {
  stats: DashboardStats;
  scheduling: SchedulingDashboard;
}

const REVENUE_DAYS = 30;
const compactCount = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    FormsModule,
    RouterLink,
    NzButtonModule,
    NzCardModule,
    NzIconModule,
    NzSegmentedModule,
    NzTableModule,
    NzTagModule,
    PageHeaderComponent,
    PageStateComponent,
    RevenueChartComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Dashboard" [subtitle]="todayLabel()" />

      <app-page-state [status]="data.status()" (retry)="data.reload()" [rows]="2"
                      errorText="We could not load today's numbers.">
        <div class="kpis">
          @for (tile of kpis(); track tile.label) {
            <div class="kpi">
              <span class="kpi__label">{{ tile.label }}</span>
              <strong class="kpi__value">{{ tile.value }}</strong>
            </div>
          }
        </div>
      </app-page-state>

      <div class="panels">
        <nz-card class="revenue" [nzTitle]="revenueTitle" [nzExtra]="revenueToggle">
          <ng-template #revenueTitle>
            <div class="card-title">
              <span>Revenue</span>
              <small>Completed appointments, last {{ revenueDays }} days</small>
            </div>
          </ng-template>
          <ng-template #revenueToggle>
            <nz-segmented [nzOptions]="revenueViews" [ngModel]="revenueView()" (ngModelChange)="revenueView.set($event)"
                          nzSize="small" aria-label="Revenue view" />
          </ng-template>

          <app-page-state [status]="data.status()" (retry)="data.reload()" [rows]="5">
            <div class="revenue__total">
              <strong>{{ revenueTotal() }}</strong>
              <span class="da-muted">total</span>
            </div>

            @if (revenueView() === 'Chart') {
              <app-revenue-chart [bars]="revenueBars()" />
            } @else {
              <nz-table [nzData]="revenueRows()" nzSize="small" [nzPageSize]="10" [nzShowPagination]="true">
                <thead><tr><th>Date</th><th nzAlign="right">Revenue</th></tr></thead>
                <tbody>
                  @for (row of revenueRows(); track row.key) {
                    <tr><td>{{ row.label }}</td><td nzAlign="right" class="num">{{ money(row.value) }}</td></tr>
                  }
                </tbody>
              </nz-table>
            }
          </app-page-state>
        </nz-card>

        <div class="side">
          <nz-card [nzTitle]="todayTitle" [nzExtra]="calendarLink" class="today">
            <ng-template #todayTitle>
              <div class="card-title"><span>Today's appointments</span></div>
            </ng-template>
            <ng-template #calendarLink><a routerLink="/appointments">Open calendar</a></ng-template>

            <app-page-state [status]="today.status()" [empty]="!today.value().length" (retry)="today.reload()"
                            emptyText="No appointments today" [rows]="3"
                            emptyActionText="Book appointment" (emptyAction)="booking.open()">
              <ul class="today__list">
                @for (item of today.value(); track item.id) {
                  <li>
                    <time>{{ time(item) }}</time>
                    <div class="today__who">
                      <strong>{{ item.patientName }}</strong>
                      <span>{{ item.doctorName }} · {{ item.serviceName }}</span>
                    </div>
                    <nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
                  </li>
                }
              </ul>
            </app-page-state>
          </nz-card>

          <nz-card class="glance">
            <app-page-state [status]="data.status()" (retry)="data.reload()" [rows]="1">
              <dl>
                @for (item of glance(); track item.label) {
                  <div><dt>{{ item.label }}</dt><dd>{{ item.value }}</dd></div>
                }
              </dl>
            </app-page-state>
          </nz-card>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .kpis { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: var(--da-space-3); }
    .kpi { display: grid; gap: 4px; padding: var(--da-space-4); background: var(--da-surface); border: 1px solid var(--da-border);
      border-radius: var(--da-radius-lg); box-shadow: var(--da-shadow); }
    .kpi__label { font-size: 13px; color: var(--da-ink-2); }
    .kpi__value { font-size: 28px; line-height: 36px; font-weight: 600; color: var(--da-ink); }

    .panels { display: grid; grid-template-columns: minmax(0, 2fr) minmax(280px, 1fr); gap: var(--da-space-4); align-items: start; }
    .side { display: grid; gap: var(--da-space-4); }
    .card-title { display: grid; line-height: 1.3; }
    .card-title small { font-size: 12px; font-weight: 400; color: var(--da-ink-3); }

    .revenue__total { display: flex; align-items: baseline; gap: var(--da-space-2); }
    .revenue__total strong { font-size: 28px; font-weight: 600; color: var(--da-ink); }
    .num { font-variant-numeric: tabular-nums; }

    .today__list { list-style: none; margin: 0; padding: 0; display: grid; }
    .today__list li { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; align-items: center; gap: var(--da-space-3);
      padding: 10px 0; border-bottom: 1px solid var(--da-border); }
    .today__list li:last-child { border-bottom: 0; }
    .today__list time { font-size: 13px; font-weight: 600; color: var(--da-ink); font-variant-numeric: tabular-nums; }
    .today__who { display: grid; min-width: 0; }
    .today__who strong, .today__who span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .today__who span { font-size: 12px; color: var(--da-ink-3); }
    .today__list nz-tag { margin: 0; }

    .glance dl { display: grid; grid-template-columns: repeat(3, 1fr); margin: 0; text-align: center; }
    .glance dl > div + div { border-inline-start: 1px solid var(--da-border); }
    .glance dt { font-size: 13px; color: var(--da-ink-2); }
    .glance dd { margin: 2px 0 0; font-size: 20px; font-weight: 600; color: var(--da-ink); }

    @media (max-width: 1200px) { .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
    @media (max-width: 1100px) { .panels { grid-template-columns: minmax(0, 1fr); } }
    @media (max-width: 560px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardPageComponent {
  private readonly dashboardService = inject(DashboardService);
  private readonly appointmentsService = inject(AppointmentsService);
  private readonly authState = inject(AuthStateService);
  readonly booking = inject(BookingService);

  readonly status = APPOINTMENT_STATUS;
  readonly revenueDays = REVENUE_DAYS;
  readonly revenueViews = ['Chart', 'Table'];
  readonly revenueView = signal<'Chart' | 'Table'>('Chart');
  readonly timeZone = computed(() => effectiveTimeZone(this.authState.user()?.clinicTimeZone));
  readonly todayLabel = computed(() => formatInZone(new Date(), this.timeZone(), { weekday: 'long', month: 'long', day: 'numeric' }));

  readonly data = remoteData<DashboardData | null>(
    () => forkJoin({ stats: this.dashboardService.getStats(), scheduling: this.dashboardService.getScheduling() }),
    null
  );

  /** "Today" is the clinic's calendar day: fetch a window around it, keep the clinic-local matches. */
  // Each appointment counts as "today" by its own clinic's calendar (platform admins see several clinics).
  // The 3-day window around the viewer's today covers every zone offset.
  readonly today = remoteData<Appointment[]>(() => {
    const windowStart = subDays(parseISO(dateKeyInZone(new Date(), this.timeZone())), 1);
    return this.appointmentsService
      .getAll(windowStart.toISOString(), addDays(windowStart, 3).toISOString())
      .pipe(map((items) => items
        .filter((item) => dateKeyInZone(item.startTime, this.zoneOf(item)) === dateKeyInZone(new Date(), this.zoneOf(item)))
        .sort((a, b) => a.startTime.localeCompare(b.startTime))));
  }, []);

  constructor() {
    onAppointmentsChanged(() => {
      this.data.reload();
      this.today.reload();
    });
  }

  time(item: Appointment): string {
    return formatAppointmentTime(item.startTime, this.zoneOf(item), this.timeZone(), { hour: 'numeric', minute: '2-digit' });
  }

  private zoneOf(item: Appointment): string {
    return item.clinicTimeZone ? effectiveTimeZone(item.clinicTimeZone) : this.timeZone();
  }

  readonly kpis = computed(() => {
    const s = this.data.value()?.scheduling;
    return [
      { label: "Today's appointments", value: s?.todayAppointments ?? 0 },
      { label: 'Upcoming', value: s?.upcomingAppointments ?? 0 },
      { label: 'Completed today', value: s?.completedToday ?? 0 },
      { label: 'Cancelled today', value: s?.cancelledToday ?? 0 },
      { label: 'No-shows today', value: s?.noShowsToday ?? 0 }
    ].map((tile) => ({ ...tile, value: compactCount.format(tile.value) }));
  });

  readonly glance = computed(() => {
    const s = this.data.value()?.stats;
    return [
      { label: 'Doctors', value: compactCount.format(s?.totalDoctors ?? 0) },
      { label: 'Patients', value: compactCount.format(s?.totalPatients ?? 0) },
      { label: 'Services', value: compactCount.format(s?.totalServices ?? 0) }
    ];
  });

  /** The API omits days with no revenue; fill every day of the window so gaps show as zero. */
  // The window ends on the clinic's today, matching the server, which buckets revenue by clinic-local date.
  readonly revenueBars = computed<RevenueBar[]>(() => {
    const byDay = new Map((this.data.value()?.scheduling.revenueByDay ?? []).map((p) => [p.period, p.revenue]));
    const end = parseISO(dateKeyInZone(new Date(), this.timeZone()));
    return Array.from({ length: REVENUE_DAYS }, (_, i) => {
      const day = subDays(end, REVENUE_DAYS - 1 - i);
      const key = format(day, 'yyyy-MM-dd');
      return { key, label: format(day, 'MMM d'), value: byDay.get(key) ?? 0 };
    });
  });

  readonly revenueRows = computed(() => [...this.revenueBars()].reverse());
  readonly revenueTotal = computed(() => formatUsd(this.revenueBars().reduce((sum, bar) => sum + bar.value, 0)));

  money(value: number): string {
    return formatUsd(value);
  }
}
