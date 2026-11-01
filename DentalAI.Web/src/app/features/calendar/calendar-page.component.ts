import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { eachDayOfInterval, format, parseISO } from 'date-fns';
import { Doctor } from '../../core/models/doctor.models';
import { CalendarAppointment, CalendarView } from '../../core/models/scheduling.models';
import { DoctorsService } from '../../core/services/doctors.service';
import { SchedulingService } from '../../core/services/scheduling.service';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { PageHeaderComponent } from '../../shared/page-header.component';

@Component({
  selector: 'app-calendar-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzSelectModule, NzTagModule, PageHeaderComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Calendar">
        <div class="filters">
          <nz-select nzAllowClear nzPlaceHolder="All doctors" [ngModel]="doctorId()"
            (ngModelChange)="doctorId.set($event); load()">
            @for (doctor of doctors(); track doctor.id) {
              <nz-option [nzValue]="doctor.id" [nzLabel]="doctor.firstName + ' ' + doctor.lastName" />
            }
          </nz-select>
          <nz-date-picker [ngModel]="selectedDate()" (ngModelChange)="selectedDate.set($event); load()" />
          <div class="views">
            @for (option of viewOptions; track option) {
              <button nz-button [nzType]="view() === option ? 'primary' : 'default'"
                (click)="view.set(option); load()">{{ option }}</button>
            }
          </div>
        </div>
      </app-page-header>

      <nz-card>
        <div class="calendar" [class.calendar--month]="view() === 'month'">
          @for (day of days(); track day.key) {
            <section class="day" [class.day--today]="day.isToday">
              <header><strong>{{ day.date | date: view() === 'month' ? 'EEE d' : 'EEEE' }}</strong>
                @if (view() !== 'month') { <span>{{ day.date | date: 'MMM d' }}</span> }
              </header>
              @for (item of day.items; track item.id) {
                <article class="appointment" [class]="'appointment status-' + item.status.toLowerCase()">
                  <time>{{ item.startTimeUtc | date: 'h:mm a' }}</time>
                  <strong>{{ item.patientName }}</strong>
                  <span>{{ item.doctorName }} · {{ item.serviceName }}</span>
                  <nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
                </article>
              } @empty { <p class="empty">No appointments</p> }
            </section>
          }
        </div>
      </nz-card>
    </section>
  `,
  styles: [`
    .filters { display: flex; align-items: center; flex-wrap: wrap; gap: var(--da-space-2); }
    .filters nz-select { min-width: 190px; }
    .views { display: flex; gap: var(--da-space-1); }
    .views button { text-transform: capitalize; }
    .calendar { display: grid; grid-template-columns: repeat(7, minmax(150px, 1fr)); gap: var(--da-space-3); overflow: auto; }
    .day { min-height: 360px; background: var(--da-surface-2); border-radius: var(--da-radius); padding: var(--da-space-3); }
    .calendar--month .day { min-height: 155px; }
    .day--today { box-shadow: inset 0 0 0 2px var(--da-brand); }
    .day > header { display: flex; justify-content: space-between; color: var(--da-ink); margin-bottom: var(--da-space-2); }
    .day > header span, .empty { color: var(--da-ink-3); font-size: 13px; }
    .empty { margin: 0; }
    /* Left edge carries status alongside the labelled tag. */
    .appointment { display: grid; gap: 2px; background: var(--da-surface); border: 1px solid var(--da-border);
      border-left: 4px solid var(--da-warning); border-radius: var(--da-radius-sm); padding: 10px; margin-bottom: var(--da-space-2); }
    .appointment time, .appointment span { font-size: 12px; color: var(--da-ink-3); }
    .appointment strong { font-size: 14px; color: var(--da-ink); }
    .appointment nz-tag { justify-self: start; margin: 4px 0 0; }
    .status-confirmed { border-left-color: var(--da-info); }
    .status-completed { border-left-color: var(--da-success); }
    .status-cancelled { border-left-color: var(--da-danger); opacity: 0.72; }
    .status-noshow { border-left-color: #d4380d; }
    @media (max-width: 900px) { .calendar { grid-template-columns: repeat(2, minmax(220px, 1fr)); } }
    @media (max-width: 560px) { .calendar { grid-template-columns: 1fr; } .filters > * { width: 100%; } .views button { flex: 1; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarPageComponent {
  private readonly scheduling = inject(SchedulingService);
  readonly status = APPOINTMENT_STATUS;
  readonly doctors = signal<Doctor[]>([]);
  readonly appointments = signal<CalendarAppointment[]>([]);
  readonly fromDate = signal(format(new Date(), 'yyyy-MM-dd'));
  readonly toDate = signal(format(new Date(), 'yyyy-MM-dd'));
  readonly selectedDate = signal(new Date());
  readonly doctorId = signal<string | undefined>(undefined);
  readonly view = signal<CalendarView>('week');
  readonly viewOptions: CalendarView[] = ['day', 'week', 'month'];

  readonly days = computed(() => eachDayOfInterval({ start: parseISO(this.fromDate()), end: parseISO(this.toDate()) }).map(date => {
    const key = format(date, 'yyyy-MM-dd');
    return {
      key, date, isToday: key === format(new Date(), 'yyyy-MM-dd'),
      items: this.appointments().filter(item => format(new Date(item.startTimeUtc), 'yyyy-MM-dd') === key)
    };
  }));

  constructor(doctorsService: DoctorsService) {
    doctorsService.getAll().subscribe(doctors => this.doctors.set(doctors));
    this.load();
  }

  load(): void {
    const date = format(this.selectedDate(), 'yyyy-MM-dd');
    this.scheduling.getCalendar(this.view(), date, this.doctorId()).subscribe(data => {
      this.fromDate.set(data.fromDate); this.toDate.set(data.toDate); this.appointments.set(data.appointments);
    });
  }
}
