import { CommonModule, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { addDays, startOfDay } from 'date-fns';
import { Appointment } from '../../core/models/appointment.models';
import { AppointmentsService } from '../../core/services/appointments.service';

@Component({
  selector: 'app-appointments-page',
  standalone: true,
  imports: [CommonModule, DatePipe, NzCardModule, NzTableModule, NzTagModule],
  template: `
    <section class="appointments">
      <nz-card nzTitle="Calendar view" class="calendar-card">
        <div class="calendar">
          @for (day of weekDays(); track day.date) {
            <section class="calendar__column">
              <header>
                <strong>{{ day.label }}</strong>
                <span>{{ day.date | date: 'MMM d' }}</span>
              </header>
              @if (day.items.length === 0) {
                <div class="calendar__empty">No appointments</div>
              } @else {
                @for (appointment of day.items; track appointment.id) {
                  <article class="calendar__item">
                    <p>{{ appointment.scheduledStartUtc | date: 'shortTime' }}</p>
                    <h3>{{ appointment.patientName }}</h3>
                    <span>{{ appointment.doctorName }} · {{ appointment.serviceName }}</span>
                  </article>
                }
              }
            </section>
          }
        </div>
      </nz-card>

      <nz-card nzTitle="Appointment ledger" class="table-card">
        <nz-table [nzData]="appointments()" [nzFrontPagination]="false">
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
            @for (appointment of appointments(); track appointment.id) {
              <tr>
                <td>{{ appointment.scheduledStartUtc | date: 'medium' }}</td>
                <td>{{ appointment.patientName }}</td>
                <td>{{ appointment.doctorName }}</td>
                <td>{{ appointment.serviceName }}</td>
                <td><nz-tag [nzColor]="statusColor(appointment.status)">{{ appointment.status }}</nz-tag></td>
              </tr>
            }
          </tbody>
        </nz-table>
      </nz-card>
    </section>
  `,
  styles: [`
    .appointments { display: grid; gap: 1rem; }
    .calendar-card, .table-card { border-radius: 1.25rem; }
    .calendar { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 0.75rem; }
    .calendar__column { background: #f7f6f2; border-radius: 1rem; padding: 0.75rem; min-height: 220px; }
    .calendar__column header { display: flex; flex-direction: column; gap: 0.2rem; margin-bottom: 0.75rem; color: #12343b; }
    .calendar__column span { color: #688084; font-size: 0.8rem; }
    .calendar__item { display: grid; gap: 0.2rem; padding: 0.65rem; border-radius: 0.85rem; background: #fff; box-shadow: inset 0 0 0 1px rgba(18, 52, 59, 0.07); margin-bottom: 0.65rem; }
    .calendar__item p, .calendar__item span { margin: 0; color: #658084; font-size: 0.78rem; }
    .calendar__item h3 { margin: 0; color: #12343b; font-size: 0.92rem; }
    .calendar__empty { color: #8ca0a3; font-size: 0.82rem; }
    @media (max-width: 1100px) { .calendar { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .calendar { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppointmentsPageComponent {
  private readonly appointmentsService = inject(AppointmentsService);
  readonly appointments = signal<Appointment[]>([]);

  readonly weekDays = computed(() => {
    const weekStart = startOfDay(new Date());
    const items = this.appointments();

    return Array.from({ length: 7 }, (_, index) => {
      const currentDate = addDays(weekStart, index);
      const currentKey = currentDate.toDateString();

      return {
        date: currentDate,
        label: currentDate.toLocaleDateString(undefined, { weekday: 'short' }),
        items: items.filter((item) => new Date(item.scheduledStartUtc).toDateString() === currentKey)
      };
    });
  });

  constructor() {
    const fromUtc = new Date().toISOString();
    const toUtc = addDays(new Date(), 7).toISOString();
    this.appointmentsService.getAll(fromUtc, toUtc).subscribe((data) => this.appointments.set(data));
  }

  statusColor(status: Appointment['status']): string {
    switch (status) {
      case 'Confirmed':
        return 'blue';
      case 'Completed':
        return 'green';
      case 'Cancelled':
        return 'red';
      case 'NoShow':
        return 'volcano';
      default:
        return 'gold';
    }
  }
}
