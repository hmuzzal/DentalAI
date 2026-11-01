import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
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
import { AuthStateService } from '../../core/services/auth-state.service';
import { BookingService, CANCELLABLE_STATUSES, onAppointmentsChanged, RESCHEDULABLE_STATUSES } from '../../core/services/booking.service';
import { AppointmentStatus } from '../../core/models/appointment.models';
import { dateKeyInZone, effectiveTimeZone, formatAppointmentTime } from '../../core/time-zone';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { PageHeaderComponent } from '../../shared/page-header.component';

@Component({
  selector: 'app-calendar-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzIconModule, NzPopconfirmModule, NzSelectModule, NzTagModule, PageHeaderComponent],
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
          <button nz-button (click)="goToToday()">Today</button>
          <nz-date-picker [ngModel]="selectedDate()" (ngModelChange)="pickDate($event)" [nzAllowClear]="false"
                          nzFormat="EEE, MMM d, yyyy" />
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
              <header>
                <span class="day__title"><strong>{{ day.date | date: view() === 'month' ? 'EEE d' : 'EEEE' }}</strong>
                  @if (view() !== 'month') { <span>{{ day.date | date: 'MMM d' }}</span> }
                </span>
                @if (!day.isPast) {
                  <button nz-button nzType="text" nzSize="small" class="day__add"
                          [attr.aria-label]="'New appointment on ' + (day.date | date: 'EEEE, MMM d')"
                          (click)="book(day.key)"><span nz-icon nzType="plus"></span></button>
                }
              </header>
              @for (item of day.items; track item.id) {
                <article class="appointment" [class]="'appointment status-' + item.status.toLowerCase()">
                  <time>{{ time(item) }}</time>
                  <strong>{{ item.patientName }}</strong>
                  <span>{{ item.doctorName }} · {{ item.serviceName }}</span>
                  <nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
                  @if (canReschedule(item.status) || canCancel(item.status)) {
                    <div class="appointment__actions">
                      @if (canReschedule(item.status)) {
                        <button nz-button nzType="link" nzSize="small" class="appointment__action" (click)="reschedule(item)"
                                [attr.aria-label]="'Reschedule ' + item.patientName">Reschedule</button>
                      }
                      @if (canCancel(item.status)) {
                        <button nz-button nzType="link" nzSize="small" nzDanger class="appointment__action"
                                nz-popconfirm nzPopconfirmTitle="Cancel this appointment?"
                                nzOkText="Cancel appointment" nzCancelText="Keep it" [nzOkDanger]="true"
                                (nzOnConfirm)="cancel(item)"
                                [attr.aria-label]="'Cancel appointment for ' + item.patientName">Cancel</button>
                      }
                    </div>
                  }
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
    .day > header { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-2);
      color: var(--da-ink); margin-bottom: var(--da-space-2); min-height: 24px; }
    .day__title { display: flex; justify-content: space-between; align-items: baseline; gap: var(--da-space-2); flex: 1; min-width: 0; }
    .day__title span, .empty { color: var(--da-ink-3); font-size: 13px; }
    .day__add { color: var(--da-ink-3); flex: none; }
    .day__add:hover, .day__add:focus-visible { color: var(--da-brand); }
    .empty { margin: 0; }
    /* Left edge carries status alongside the labelled tag. */
    .appointment { display: grid; gap: 2px; background: var(--da-surface); border: 1px solid var(--da-border);
      border-left: 4px solid var(--da-warning); border-radius: var(--da-radius-sm); padding: 10px; margin-bottom: var(--da-space-2); }
    .appointment time, .appointment span { font-size: 12px; color: var(--da-ink-3); }
    .appointment strong { font-size: 14px; color: var(--da-ink); }
    .appointment nz-tag { justify-self: start; margin: 4px 0 0; }
    .appointment__actions { display: flex; flex-wrap: wrap; gap: var(--da-space-3); margin-top: 4px; }
    .appointment__action { padding: 0; height: auto; }
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
  private readonly authState = inject(AuthStateService);
  private readonly booking = inject(BookingService);
  readonly status = APPOINTMENT_STATUS;
  readonly doctors = signal<Doctor[]>([]);
  readonly appointments = signal<CalendarAppointment[]>([]);
  readonly doctorId = signal<string | undefined>(undefined);
  readonly view = signal<CalendarView>('week');
  readonly viewOptions: CalendarView[] = ['day', 'week', 'month'];

  // Days and times follow the clinic's time zone, not the browser's.
  readonly timeZone = computed(() => effectiveTimeZone(this.authState.user()?.clinicTimeZone));

  /** "Today" on the clinic's calendar, as a local Date for the date picker. */
  private clinicToday(): Date {
    return parseISO(dateKeyInZone(new Date(), this.timeZone()));
  }

  readonly fromDate = signal(format(this.clinicToday(), 'yyyy-MM-dd'));
  readonly toDate = signal(format(this.clinicToday(), 'yyyy-MM-dd'));
  readonly selectedDate = signal(this.clinicToday());
  /** Set once the user picks a date; until then the calendar follows the clinic's "today". */
  private dateChosen = false;

  readonly days = computed(() => {
    const zone = this.timeZone();
    const todayKey = dateKeyInZone(new Date(), zone);
    return eachDayOfInterval({ start: parseISO(this.fromDate()), end: parseISO(this.toDate()) }).map(date => {
      const key = format(date, 'yyyy-MM-dd');
      return {
        key, date, isToday: key === todayKey, isPast: key < todayKey,
        // Each appointment is placed in its own clinic's calendar day (platform admins see several clinics).
        items: this.appointments().filter(item => dateKeyInZone(item.startTimeUtc, this.zoneOf(item)) === key)
      };
    });
  });

  constructor(doctorsService: DoctorsService) {
    doctorsService.getAll().subscribe(doctors => this.doctors.set(doctors));
    // Loads on open, and again if the clinic zone arrives later (profile still loading after a refresh),
    // so the first view is the clinic's today rather than the browser's.
    effect(() => {
      this.timeZone();
      untracked(() => {
        if (!this.dateChosen) this.selectedDate.set(this.clinicToday());
        this.load();
      });
    });
    onAppointmentsChanged(() => this.load());
  }

  pickDate(date: Date | null): void {
    if (!date) return;
    this.dateChosen = true;
    this.selectedDate.set(date);
    this.load();
  }

  goToToday(): void {
    this.dateChosen = false;
    this.selectedDate.set(this.clinicToday());
    this.load();
  }

  time(item: CalendarAppointment): string {
    return formatAppointmentTime(item.startTimeUtc, this.zoneOf(item), this.timeZone(), { hour: 'numeric', minute: '2-digit' });
  }

  private zoneOf(item: CalendarAppointment): string {
    return item.clinicTimeZone ? effectiveTimeZone(item.clinicTimeZone) : this.timeZone();
  }

  canCancel(status: AppointmentStatus): boolean {
    return (CANCELLABLE_STATUSES as readonly string[]).includes(status);
  }

  cancel(item: CalendarAppointment): void {
    this.booking.cancel(item);
  }

  book(dateKey: string): void {
    this.booking.open({ date: dateKey, doctorId: this.doctorId() ?? null });
  }

  canReschedule(status: AppointmentStatus): boolean {
    return (RESCHEDULABLE_STATUSES as readonly string[]).includes(status);
  }

  reschedule(item: CalendarAppointment): void {
    this.booking.reschedule({ ...item, startTime: item.startTimeUtc });
  }

  load(): void {
    const date = format(this.selectedDate(), 'yyyy-MM-dd');
    this.scheduling.getCalendar(this.view(), date, this.doctorId()).subscribe(data => {
      this.fromDate.set(data.fromDate); this.toDate.set(data.toDate); this.appointments.set(data.appointments);
    });
  }
}
