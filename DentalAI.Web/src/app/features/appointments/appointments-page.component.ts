import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { addDays, startOfDay } from 'date-fns';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { Appointment } from '../../core/models/appointment.models';
import { AppointmentsService } from '../../core/services/appointments.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-appointments-page',
  standalone: true,
  imports: [DatePipe, NzCardModule, NzTableModule, NzTagModule, PageHeaderComponent, PageStateComponent],
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
                  <strong>{{ day.date | date: 'EEE' }}</strong>
                  <span>{{ day.date | date: 'MMM d' }}</span>
                </header>
                @for (appointment of day.items; track appointment.id) {
                  <article class="week__item">
                    <time>{{ appointment.startTime | date: 'h:mm a' }}</time>
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
                        errorText="We could not load appointments.">
          <nz-table [nzData]="appointments.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Start</th>
                <th>Patient</th>
                <th>Doctor</th>
                <th>Service</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (appointment of appointments.value(); track appointment.id) {
                <tr>
                  <td class="num">{{ appointment.startTime | date: 'EEE, MMM d · h:mm a' }}</td>
                  <td><strong>{{ appointment.patientName }}</strong></td>
                  <td>{{ appointment.doctorName }}</td>
                  <td>{{ appointment.serviceName }}</td>
                  <td>
                    <nz-tag [nzColor]="status[appointment.status].color">{{ status[appointment.status].label }}</nz-tag>
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
    .week__day header { display: grid; gap: 2px; margin-bottom: var(--da-space-3); }
    .week__day header strong { color: var(--da-ink); }
    .week__day header span { color: var(--da-ink-3); font-size: 12px; }
    .week__item { display: grid; gap: 2px; padding: 10px; margin-bottom: var(--da-space-2); background: var(--da-surface);
      border: 1px solid var(--da-border); border-radius: var(--da-radius-sm); }
    .week__item time, .week__item span { font-size: 12px; color: var(--da-ink-3); }
    .week__item strong { font-size: 14px; color: var(--da-ink); }
    .week__empty { margin: 0; color: var(--da-ink-3); font-size: 13px; }
    .num { font-variant-numeric: tabular-nums; white-space: nowrap; }
    @media (max-width: 1100px) { .week { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .week { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppointmentsPageComponent {
  private readonly appointmentsService = inject(AppointmentsService);
  readonly status = APPOINTMENT_STATUS;

  readonly appointments = remoteData<Appointment[]>(
    () => this.appointmentsService.getAll(new Date().toISOString(), addDays(new Date(), 7).toISOString()),
    []
  );

  // TODO(phase 1): group by the clinic's time zone instead of the browser's.
  readonly weekDays = computed(() => {
    const weekStart = startOfDay(new Date());
    const items = this.appointments.value();

    return Array.from({ length: 7 }, (_, index) => {
      const date = addDays(weekStart, index);
      const key = date.toDateString();
      return { key, date, items: items.filter((item) => new Date(item.startTime).toDateString() === key) };
    });
  });
}
