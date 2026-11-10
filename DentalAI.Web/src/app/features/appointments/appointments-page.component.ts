import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { addDays, addMonths, eachDayOfInterval, format, isValid, parseISO } from 'date-fns';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { finalize, map, of, Subscription, switchMap } from 'rxjs';
import { InvoicesService } from '../../core/services/invoices.service';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { AppointmentStatus } from '../../core/models/appointment.models';
import { Doctor } from '../../core/models/doctor.models';
import { CalendarAppointment, CalendarView } from '../../core/models/scheduling.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { BookingService, CANCELLABLE_STATUSES, onAppointmentsChanged, RESCHEDULABLE_STATUSES } from '../../core/services/booking.service';
import { DoctorsService } from '../../core/services/doctors.service';
import { SchedulingService } from '../../core/services/scheduling.service';
import { dateKeyInZone, effectiveTimeZone, formatAppointmentTime } from '../../core/time-zone';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { LoadStatus } from '../../shared/remote-data';

type Layout = 'calendar' | 'list';

interface Day {
  key: string;
  date: Date;
  isToday: boolean;
  isPast: boolean;
  items: CalendarAppointment[];
}

const VIEWS: CalendarView[] = ['day', 'week', 'month'];
const LAYOUTS: Layout[] = ['calendar', 'list'];

/**
 * Appointments and calendar in one page. Range (day / week / month), layout (calendar / list), date and doctor
 * live in the URL (?view=week&layout=calendar&date=2026-11-01&doctor=...), so Back and shared links work.
 * Weeks run Sunday to Saturday. Days and times follow each appointment's clinic time zone.
 */
@Component({
  selector: 'app-appointments-page',
  standalone: true,
  imports: [
    DatePipe, FormsModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzIconModule, NzPopconfirmModule,
    NzSegmentedModule, NzSelectModule, NzTableModule, NzTagModule, PageHeaderComponent, PageStateComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Appointments" [subtitle]="rangeLabel()">
        <nz-segmented [nzOptions]="layoutOptions" [ngModel]="layout()" (ngModelChange)="setLayout($event)"
                      aria-label="Show as" />
      </app-page-header>

      <div class="toolbar">
        <div class="nav">
          <button nz-button (click)="goToToday()" [disabled]="isTodayInRange()">Today</button>
          <button nz-button nzType="text" (click)="step(-1)" [attr.aria-label]="'Previous ' + view()">
            <span nz-icon nzType="left"></span>
          </button>
          <button nz-button nzType="text" (click)="step(1)" [attr.aria-label]="'Next ' + view()">
            <span nz-icon nzType="right"></span>
          </button>
          <nz-date-picker [ngModel]="selectedDate()" (ngModelChange)="pickDate($event)" [nzAllowClear]="false"
                          nzFormat="EEE, MMM d, yyyy" aria-label="Go to date" />
        </div>
        <div class="filters">
          <nz-select class="doctor" nzAllowClear nzShowSearch nzPlaceHolder="All doctors" [ngModel]="doctorId()"
                     (ngModelChange)="setDoctor($event)" aria-label="Doctor">
            @for (doctor of doctors(); track doctor.id) {
              <nz-option [nzValue]="doctor.id" [nzLabel]="doctor.firstName + ' ' + doctor.lastName" />
            }
          </nz-select>
          <nz-segmented [nzOptions]="viewOptions" [ngModel]="viewLabel()" (ngModelChange)="setView($event)"
                        aria-label="Range" />
        </div>
      </div>

      <nz-card>
        <app-page-state [status]="loadStatus()" (retry)="load()" [rows]="6" errorText="We could not load appointments.">
          @if (layout() === 'calendar') {
            <div class="calendar" [class.calendar--day]="view() === 'day'" [class.calendar--month]="view() === 'month'"
                 [class.is-loading]="fetching()">
              @if (view() !== 'day') {
                @for (name of weekdayNames; track name) { <div class="weekday" aria-hidden="true">{{ name }}</div> }
              }
              @for (blank of leadingBlanks(); track $index) { <div class="day day--blank" aria-hidden="true"></div> }
              @for (day of days(); track day.key) {
                <section class="day" [class.day--today]="day.isToday" [class.day--past]="day.isPast"
                         [attr.aria-label]="(day.date | date: 'EEEE, MMMM d') + ', ' + day.items.length + ' appointments'">
                  <header>
                    <span class="day__title">
                      @if (view() === 'day') { <strong>{{ day.date | date: 'EEEE, MMMM d' }}</strong> }
                      @else if (view() === 'month') { <strong>{{ day.date | date: 'd' }}</strong> }
                      @else { <strong>{{ day.date | date: 'MMM d' }}</strong> }
                    </span>
                    @if (!day.isPast) {
                      <button nz-button nzType="text" nzSize="small" class="day__add"
                              [attr.aria-label]="'New appointment on ' + (day.date | date: 'EEEE, MMM d')"
                              (click)="book(day.key)"><span nz-icon nzType="plus"></span></button>
                    }
                  </header>

                  @for (item of visibleItems(day); track item.id) {
                    <article class="appointment" [class]="'appointment status-' + item.status.toLowerCase()">
                      <time>{{ time(item) }}</time>
                      <strong>{{ item.patientName }}</strong>
                      @if (view() !== 'month') {
                        <span>{{ item.doctorName }} · {{ item.serviceName }}</span>
                        <nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
                        @if (item.source === 'Portal') { <nz-tag nzColor="purple" title="Booked by the patient online">Online</nz-tag> }
                        <div class="appointment__actions">
                          @if (item.status === 'Completed') {
                            <button nz-button nzType="link" nzSize="small" (click)="openInvoice(item)" [nzLoading]="invoicingId() === item.id"
                                    [attr.aria-label]="'Invoice for ' + item.patientName">Invoice</button>
                          }
                          @if (canReschedule(item.status)) {
                            <button nz-button nzType="link" nzSize="small" (click)="reschedule(item)"
                                    [attr.aria-label]="'Reschedule ' + item.patientName">Reschedule</button>
                          }
                          @if (canCancel(item.status)) {
                            <button nz-button nzType="link" nzSize="small" nzDanger
                                    nz-popconfirm nzPopconfirmTitle="Cancel this appointment?"
                                    nzOkText="Cancel appointment" nzCancelText="Keep it" [nzOkDanger]="true"
                                    (nzOnConfirm)="cancel(item)"
                                    [attr.aria-label]="'Cancel appointment for ' + item.patientName">Cancel</button>
                          }
                        </div>
                      }
                    </article>
                  } @empty {
                    @if (view() !== 'month') { <p class="empty">No appointments</p> }
                  }
                  @if (hiddenCount(day); as more) {
                    <button nz-button nzType="link" nzSize="small" class="more" (click)="openDay(day.key)">
                      +{{ more }} more
                    </button>
                  }
                </section>
              }
            </div>
          } @else {
            <nz-table [nzData]="sortedAppointments()" [nzPageSize]="25" [nzHideOnSinglePage]="true" nzSize="middle"
                      [nzLoading]="fetching()" [nzNoResult]="noAppointments">
              <thead>
                <tr>
                  <th>Start</th>
                  <th>Patient</th>
                  <th>Doctor</th>
                  <th>Service</th>
                  <th>Status</th>
                  <th nzAlign="right"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (item of sortedAppointments(); track item.id) {
                  <tr>
                    <td class="num">{{ dateTime(item) }}</td>
                    <td><strong>{{ item.patientName }}</strong></td>
                    <td>{{ item.doctorName }}</td>
                    <td>{{ item.serviceName }}</td>
                    <td><nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
                      @if (item.source === 'Portal') { <nz-tag nzColor="purple">Online</nz-tag> }</td>
                    <td nzAlign="right" class="nowrap">
                      @if (item.status === 'Completed') {
                        <button nz-button nzType="link" nzSize="small" (click)="openInvoice(item)" [nzLoading]="invoicingId() === item.id"
                                [attr.aria-label]="'Invoice for ' + item.patientName">Invoice</button>
                      }
                      @if (canReschedule(item.status)) {
                        <button nz-button nzType="link" nzSize="small" (click)="reschedule(item)"
                                [attr.aria-label]="'Reschedule ' + item.patientName">Reschedule</button>
                      }
                      @if (canCancel(item.status)) {
                        <button nz-button nzType="link" nzSize="small" nzDanger
                                nz-popconfirm nzPopconfirmTitle="Cancel this appointment?" nzPopconfirmPlacement="left"
                                nzOkText="Cancel appointment" nzCancelText="Keep it" [nzOkDanger]="true"
                                (nzOnConfirm)="cancel(item)"
                                [attr.aria-label]="'Cancel appointment for ' + item.patientName">Cancel</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </nz-table>
            <ng-template #noAppointments>
              <div class="none">
                <p>No appointments {{ view() === 'day' ? 'on this day' : 'this ' + view() }}.</p>
                <button nz-button nzType="primary" (click)="book()">Book appointment</button>
              </div>
            </ng-template>
          }
        </app-page-state>
      </nz-card>
    </section>
  `,
  styles: [`
    .toolbar { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-3); flex-wrap: wrap; }
    .nav, .filters { display: flex; align-items: center; gap: var(--da-space-2); flex-wrap: wrap; }
    .doctor { min-width: 200px; }
    .calendar { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: var(--da-space-2); transition: opacity .15s; }
    .calendar.is-loading { opacity: .55; }
    .calendar--day { grid-template-columns: 1fr; }
    .weekday { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .06em; color: var(--da-ink-3); padding: 0 var(--da-space-2); }
    .day { min-height: 320px; background: var(--da-surface-2); border-radius: var(--da-radius); padding: var(--da-space-2); min-width: 0; }
    .calendar--day .day { min-height: 240px; }
    .calendar--month .day { min-height: 118px; }
    .day--blank { background: transparent; }
    .day--past { background: color-mix(in srgb, var(--da-surface-2) 60%, transparent); }
    .day--today { box-shadow: inset 0 0 0 2px var(--da-brand); }
    .day > header { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-1); min-height: 24px; margin-bottom: var(--da-space-1); }
    .day__title strong { color: var(--da-ink); font-variant-numeric: tabular-nums; }
    .day--today .day__title strong { color: var(--da-brand); }
    .day__add { color: var(--da-ink-3); }
    .day__add:hover, .day__add:focus-visible { color: var(--da-brand); }
    .empty { margin: 0; color: var(--da-ink-3); font-size: 13px; }
    /* The left edge carries the status alongside the labelled tag. */
    .appointment { display: grid; gap: 2px; background: var(--da-surface); border: 1px solid var(--da-border);
      border-left: 4px solid var(--da-warning); border-radius: var(--da-radius-sm); padding: 8px; margin-bottom: var(--da-space-1); min-width: 0; }
    .appointment time, .appointment span { font-size: 12px; color: var(--da-ink-3); }
    .appointment strong { font-size: 13px; color: var(--da-ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .appointment nz-tag { justify-self: start; margin: 4px 0 0; }
    .appointment__actions { display: flex; flex-wrap: wrap; gap: var(--da-space-2); }
    .appointment__actions button { padding: 0; height: auto; }
    .calendar--month .appointment { padding: 4px 6px; grid-template-columns: auto 1fr; gap: 6px; align-items: baseline; }
    .calendar--day .appointment { grid-template-columns: 90px 1fr auto; column-gap: var(--da-space-3); align-items: center; }
    .calendar--day .appointment span { grid-column: 2; }
    .calendar--day .appointment nz-tag { grid-column: 3; grid-row: 1; margin: 0; }
    .calendar--day .appointment__actions { grid-column: 3; justify-content: flex-end; }
    .more { padding: 0; height: auto; font-size: 12px; }
    .status-confirmed { border-left-color: var(--da-info); }
    .status-completed { border-left-color: var(--da-success); }
    .status-cancelled { border-left-color: var(--da-danger); opacity: .72; }
    .status-noshow { border-left-color: #d4380d; }
    .num { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .nowrap { white-space: nowrap; }
    .none { padding: var(--da-space-4) 0; text-align: center; color: var(--da-ink-2); }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    @media (max-width: 900px) {
      .calendar:not(.calendar--month) { grid-template-columns: 1fr; }
      .calendar:not(.calendar--month) .weekday, .calendar:not(.calendar--month) .day--blank { display: none; }
      .day { min-height: 0; }
      .calendar--month .appointment strong { display: none; }
    }
    @media (max-width: 560px) { .toolbar, .nav, .filters { width: 100%; } .doctor { flex: 1; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppointmentsPageComponent {
  private readonly scheduling = inject(SchedulingService);
  private readonly authState = inject(AuthStateService);
  private readonly booking = inject(BookingService);
  private readonly router = inject(Router);
  private readonly invoices = inject(InvoicesService);
  readonly invoicingId = signal<string | null>(null);
  private readonly route = inject(ActivatedRoute);

  readonly status = APPOINTMENT_STATUS;
  readonly weekdayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  readonly viewOptions = ['Day', 'Week', 'Month'];
  readonly layoutOptions = [
    { label: 'Calendar', value: 'calendar', icon: 'calendar' },
    { label: 'List', value: 'list', icon: 'unordered-list' }
  ];

  readonly doctors = signal<Doctor[]>([]);
  readonly appointments = signal<CalendarAppointment[]>([]);
  readonly fromDate = signal<string | null>(null);
  readonly toDate = signal<string | null>(null);
  readonly loadStatus = signal<LoadStatus>('loading');
  readonly fetching = signal(false);

  // Days and times follow the clinic's time zone, not the browser's.
  readonly timeZone = computed(() => effectiveTimeZone(this.authState.user()?.clinicTimeZone));

  // URL state
  private readonly params = this.route.snapshot.queryParamMap;
  readonly view = signal<CalendarView>(pick(this.params.get('view'), VIEWS, 'week'));
  readonly layout = signal<Layout>(pick(this.params.get('layout'), LAYOUTS, 'calendar'));
  readonly doctorId = signal<string | null>(this.params.get('doctor'));
  private readonly dateParam = parseDate(this.params.get('date'));
  readonly selectedDate = signal<Date>(this.dateParam ?? this.clinicToday());
  /** Until the user picks a date, the page follows the clinic's "today" (the zone can arrive after load). */
  private dateChosen = !!this.dateParam;

  readonly viewLabel = computed(() => capitalize(this.view()));
  private request?: Subscription;

  readonly days = computed<Day[]>(() => {
    const from = this.fromDate();
    const to = this.toDate();
    if (!from || !to) return [];
    const todayKey = dateKeyInZone(new Date(), this.timeZone());
    return eachDayOfInterval({ start: parseISO(from), end: parseISO(to) }).map((date) => {
      const key = format(date, 'yyyy-MM-dd');
      return {
        key, date, isToday: key === todayKey, isPast: key < todayKey,
        // Each appointment sits on its own clinic's calendar day (platform admins see several clinics).
        items: this.sortedAppointments().filter((item) => dateKeyInZone(item.startTimeUtc, this.zoneOf(item)) === key)
      };
    });
  });

  /** Empty cells before the 1st so month columns line up with Sun–Sat. */
  readonly leadingBlanks = computed(() =>
    this.view() === 'month' && this.days().length ? Array(this.days()[0].date.getDay()).fill(0) : []);

  readonly sortedAppointments = computed(() =>
    [...this.appointments()].sort((a, b) => a.startTimeUtc.localeCompare(b.startTimeUtc)));

  readonly isTodayInRange = computed(() => this.days().some((d) => d.isToday));

  readonly rangeLabel = computed(() => {
    const days = this.days();
    if (!days.length) return '';
    const first = days[0].date;
    const last = days[days.length - 1].date;
    switch (this.view()) {
      case 'day': return format(first, 'EEEE, MMMM d, yyyy');
      case 'month': return format(first, 'MMMM yyyy');
      default:
        return first.getFullYear() === last.getFullYear()
          ? `${format(first, 'MMM d')} – ${format(last, 'MMM d, yyyy')}`
          : `${format(first, 'MMM d, yyyy')} – ${format(last, 'MMM d, yyyy')}`;
    }
  });

  constructor(doctorsService: DoctorsService) {
    doctorsService.getAll().subscribe((doctors) => this.doctors.set(doctors));

    // Load on open, and again if the clinic zone arrives later (profile still loading after a refresh).
    effect(() => {
      this.timeZone();
      untracked(() => {
        if (!this.dateChosen) this.selectedDate.set(this.clinicToday());
        this.load();
      });
    });
    onAppointmentsChanged(() => this.load());
  }

  // ---- Navigation ----

  setView(label: string): void {
    this.view.set(pick(label.toLowerCase(), VIEWS, 'week'));
    this.load();
  }

  setLayout(layout: Layout): void {
    this.layout.set(layout);
    this.syncUrl();
  }

  setDoctor(id: string | null): void {
    this.doctorId.set(id || null);
    this.load();
  }

  pickDate(date: Date | null): void {
    if (!date) return;
    this.dateChosen = true;
    this.selectedDate.set(date);
    this.load();
  }

  step(direction: 1 | -1): void {
    const date = this.selectedDate();
    const view = this.view();
    this.pickDate(view === 'month' ? addMonths(date, direction) : addDays(date, direction * (view === 'week' ? 7 : 1)));
  }

  goToToday(): void {
    this.dateChosen = false;
    this.selectedDate.set(this.clinicToday());
    this.load();
  }

  openDay(key: string): void {
    this.dateChosen = true;
    this.selectedDate.set(parseISO(key));
    this.view.set('day');
    this.load();
  }

  // ---- Data ----

  load(): void {
    this.syncUrl();
    this.fetching.set(true);
    this.request?.unsubscribe();
    this.request = this.scheduling
      .getCalendar(this.view(), format(this.selectedDate(), 'yyyy-MM-dd'), this.doctorId() ?? undefined)
      .subscribe({
        next: (data) => {
          this.fromDate.set(data.fromDate);
          this.toDate.set(data.toDate);
          this.appointments.set(data.appointments);
          this.loadStatus.set('ready');
          this.fetching.set(false);
        },
        error: () => {
          this.fetching.set(false);
          if (this.loadStatus() !== 'ready') this.loadStatus.set('error');
        }
      });
  }

  /** Month cells show at most 3 appointments; the rest are behind "+N more" (opens the day). */
  visibleItems(day: Day): CalendarAppointment[] {
    return this.view() === 'month' ? day.items.slice(0, 3) : day.items;
  }

  hiddenCount(day: Day): number {
    return this.view() === 'month' ? Math.max(0, day.items.length - 3) : 0;
  }

  // ---- Formatting ----

  time(item: CalendarAppointment): string {
    return formatAppointmentTime(item.startTimeUtc, this.zoneOf(item), this.timeZone(), { hour: 'numeric', minute: '2-digit' });
  }

  dateTime(item: CalendarAppointment): string {
    return formatAppointmentTime(item.startTimeUtc, this.zoneOf(item), this.timeZone(),
      { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  }

  // ---- Actions ----

  book(dateKey?: string): void {
    this.booking.open({ date: dateKey ?? null, doctorId: this.doctorId() });
  }

  canReschedule(status: AppointmentStatus): boolean {
    return (RESCHEDULABLE_STATUSES as readonly string[]).includes(status);
  }

  reschedule(item: CalendarAppointment): void {
    this.booking.reschedule({ ...item, startTime: item.startTimeUtc });
  }

  canCancel(status: AppointmentStatus): boolean {
    return (CANCELLABLE_STATUSES as readonly string[]).includes(status);
  }

  /** Opens the appointment's invoice, creating a draft (one line for its service) if it has none yet. */
  openInvoice(item: CalendarAppointment): void {
    this.invoicingId.set(item.id);
    this.invoices.list({ appointmentId: item.id, page: 1, pageSize: 1 }).pipe(
      switchMap((list) => list.items.length ? of(list.items[0].id) : this.invoices.createFromAppointment(item.id).pipe(map((inv) => inv.id))),
      finalize(() => this.invoicingId.set(null))
    ).subscribe({ next: (id) => void this.router.navigate(['/invoices', id]) });
  }

  cancel(item: CalendarAppointment): void {
    this.booking.cancel(item);
  }

  // ---- Helpers ----

  private zoneOf(item: CalendarAppointment): string {
    return item.clinicTimeZone ? effectiveTimeZone(item.clinicTimeZone) : this.timeZone();
  }

  /** "Today" on the clinic's calendar, as a local Date. */
  private clinicToday(): Date {
    return parseISO(dateKeyInZone(new Date(), this.timeZone()));
  }

  private syncUrl(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        view: this.view(),
        layout: this.layout(),
        date: this.dateChosen ? format(this.selectedDate(), 'yyyy-MM-dd') : null,
        doctor: this.doctorId()
      }
    });
  }
}

function pick<T extends string>(value: string | null, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? (value as T) : fallback;
}

function parseDate(value: string | null): Date | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const date = parseISO(value);
  return isValid(date) ? date : null;
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
