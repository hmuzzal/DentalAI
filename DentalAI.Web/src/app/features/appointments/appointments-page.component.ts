import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { addDays, format, parseISO } from 'date-fns';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { AuthStateService } from '../../core/services/auth-state.service';
import { BookingService, CANCELLABLE_STATUSES, onAppointmentsChanged, RESCHEDULABLE_STATUSES } from '../../core/services/booking.service';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { dateKeyInZone, effectiveTimeZone, formatAppointmentTime } from '../../core/time-zone';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { Appointment, AppointmentStatus } from '../../core/models/appointment.models';
import { AppointmentsService } from '../../core/services/appointments.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-appointments-page',
  standalone: true,
  imports: [DatePipe, NzButtonModule, NzCardModule, NzIconModule, NzPopconfirmModule, NzTableModule, NzTagModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Appointments" subtitle="The next 7 days." />

      <nz-card nzTitle="Week at a glance">
        <app-page-state [status]="appointments.status()" (retry)="appointments.reload()" [rows]="3"
                        errorText="We could not load appointments.">
          <div class="week">
            @for (day of weekDays(); track day.key) {
              <section class="week__day">
                <header>
                  <span class="week__title">
                    <strong>{{ day.date | date: 'EEE' }}</strong>
                    <span>{{ day.date | date: 'MMM d' }}</span>
                  </span>
                  <button nz-button nzType="text" nzSize="small" class="week__add"
                          [attr.aria-label]="'New appointment on ' + (day.date | date: 'EEEE, MMM d')"
                          (click)="book(day.key)"><span nz-icon nzType="plus"></span></button>
                </header>
                @for (appointment of day.items; track appointment.id) {
                  <article class="week__item">
                    <time>{{ time(appointment) }}</time>
                    <strong>{{ appointment.patientName }}</strong>
                    <span>{{ appointment.doctorName }} · {{ appointment.serviceName }}</span>
                  </article>
                } @empty {
                  <p class="week__empty">No appointments</p>
                }
              </section>
            }
          </div>
        </app-page-state>
      </nz-card>

      <nz-card nzTitle="All appointments">
        <app-page-state [status]="appointments.status()" [empty]="!appointments.value().length"
                        (retry)="appointments.reload()" emptyText="No appointments in the next 7 days"
                        emptyActionText="Book appointment" (emptyAction)="book()"
                        errorText="We could not load appointments.">
          <nz-table [nzData]="appointments.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Start</th>
                <th>Patient</th>
                <th>Doctor</th>
                <th>Service</th>
                <th>Status</th>
                <th><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (appointment of appointments.value(); track appointment.id) {
                <tr>
                  <td class="num">{{ dateTime(appointment) }}</td>
                  <td><strong>{{ appointment.patientName }}</strong></td>
                  <td>{{ appointment.doctorName }}</td>
                  <td>{{ appointment.serviceName }}</td>
                  <td>
                    <nz-tag [nzColor]="status[appointment.status].color">{{ status[appointment.status].label }}</nz-tag>
                  </td>
                  <td class="actions">
                    @if (canReschedule(appointment.status)) {
                      <button nz-button nzType="link" nzSize="small" (click)="reschedule(appointment)"
                              [attr.aria-label]="'Reschedule ' + appointment.patientName">Reschedule</button>
                    }
                    @if (canCancel(appointment.status)) {
                      <button nz-button nzType="link" nzSize="small" nzDanger
                              nz-popconfirm nzPopconfirmTitle="Cancel this appointment?" nzPopconfirmPlacement="left"
                              nzOkText="Cancel appointment" nzCancelText="Keep it" [nzOkDanger]="true"
                              (nzOnConfirm)="cancel(appointment)"
                              [attr.aria-label]="'Cancel appointment for ' + appointment.patientName">Cancel</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
        </app-page-state>
      </nz-card>
    </section>
  `,
  styles: [`
    .week { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: var(--da-space-3); }
    .week__day { background: var(--da-surface-2); border-radius: var(--da-radius); padding: var(--da-space-3); min-height: 200px; }
    .week__day header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: var(--da-space-3); }
    .week__title { display: grid; gap: 2px; }
    .week__title strong { color: var(--da-ink); }
    .week__title span { color: var(--da-ink-3); font-size: 12px; }
    .week__add { color: var(--da-ink-3); }
    .week__add:hover, .week__add:focus-visible { color: var(--da-brand); }
    .week__item { display: grid; gap: 2px; padding: 10px; margin-bottom: var(--da-space-2); background: var(--da-surface);
      border: 1px solid var(--da-border); border-radius: var(--da-radius-sm); }
    .week__item time, .week__item span { font-size: 12px; color: var(--da-ink-3); }
    .week__item strong { font-size: 14px; color: var(--da-ink); }
    .week__empty { margin: 0; color: var(--da-ink-3); font-size: 13px; }
    .num { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .actions { text-align: right; white-space: nowrap; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    @media (max-width: 1100px) { .week { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .week { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppointmentsPageComponent {
  private readonly appointmentsService = inject(AppointmentsService);
  private readonly authState = inject(AuthStateService);
  private readonly booking = inject(BookingService);
  readonly status = APPOINTMENT_STATUS;
  readonly timeZone = computed(() => effectiveTimeZone(this.authState.user()?.clinicTimeZone));

  readonly appointments = remoteData<Appointment[]>(
    () => this.appointmentsService.getAll(new Date().toISOString(), addDays(new Date(), 7).toISOString()),
    []
  );

  constructor() {
    onAppointmentsChanged(() => this.appointments.reload());
  }

  // Each appointment is placed and timed in its own clinic's zone (platform admins see several clinics).
  // The week columns follow the viewer's clinic calendar.
  readonly weekDays = computed(() => {
    const today = parseISO(dateKeyInZone(new Date(), this.timeZone()));
    const items = this.appointments.value();

    return Array.from({ length: 7 }, (_, index) => {
      const date = addDays(today, index);
      const key = format(date, 'yyyy-MM-dd');
      return { key, date, items: items.filter((item) => dateKeyInZone(item.startTime, this.zoneOf(item)) === key) };
    });
  });

  time(appointment: Appointment): string {
    return formatAppointmentTime(appointment.startTime, this.zoneOf(appointment), this.timeZone(),
      { hour: 'numeric', minute: '2-digit' });
  }

  dateTime(appointment: Appointment): string {
    return formatAppointmentTime(appointment.startTime, this.zoneOf(appointment), this.timeZone(),
      { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  }

  private zoneOf(appointment: Appointment): string {
    return appointment.clinicTimeZone ? effectiveTimeZone(appointment.clinicTimeZone) : this.timeZone();
  }

  book(dateKey?: string): void {
    this.booking.open({ date: dateKey ?? null });
  }

  canReschedule(status: AppointmentStatus): boolean {
    return (RESCHEDULABLE_STATUSES as readonly string[]).includes(status);
  }

  reschedule(appointment: Appointment): void {
    this.booking.reschedule(appointment);
  }

  canCancel(status: AppointmentStatus): boolean {
    return (CANCELLABLE_STATUSES as readonly string[]).includes(status);
  }

  cancel(appointment: Appointment): void {
    this.booking.cancel(appointment);
  }
}
