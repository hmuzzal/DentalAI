import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { format, parseISO } from 'date-fns';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzDropDownModule } from 'ng-zorro-antd/dropdown';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule, NzModalService } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { finalize } from 'rxjs';
import { Doctor } from '../../core/models/doctor.models';
import { DoctorsService } from '../../core/services/doctors.service';
import { SchedulingService } from '../../core/services/scheduling.service';
import { saveErrorText } from '../../shared/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';
import {
  DaySchedule, DEFAULT_BREAK, DEFAULT_HOURS, fromAvailability, minutes, TimeRange, toAvailability, validate,
  WeekDay, WEEK_DAYS, WeeklySchedule, weeklyHours, WORK_WEEK
} from './weekly-schedule';

/** The timeline bar covers 6 AM – 10 PM; anything outside is clipped to the edges. */
const TIMELINE_START = 6 * 60;
const TIMELINE_END = 22 * 60;

/**
 * A doctor's week as a grid: one row per day with open/closed, working hours, breaks and a timeline bar,
 * plus time off. Times are wall-clock times in the clinic's time zone. ?doctor=<id> selects the doctor.
 */
@Component({
  selector: 'app-doctor-schedules-page',
  standalone: true,
  imports: [
    FormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzDropDownModule, NzEmptyModule,
    NzIconModule, NzInputModule, NzModalModule, NzSelectModule, NzSkeletonModule, NzSwitchModule, PageHeaderComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Doctor schedules"
                       subtitle="Weekly hours, breaks and time off. Times are in the clinic's time zone.">
        <nz-select class="doctor-picker" nzPlaceHolder="Select a doctor" nzShowSearch [ngModel]="doctorId()"
                   (ngModelChange)="selectDoctor($event)" aria-label="Doctor">
          @for (doctor of doctors(); track doctor.id) {
            <nz-option [nzValue]="doctor.id" [nzLabel]="doctor.firstName + ' ' + doctor.lastName" />
          }
        </nz-select>
      </app-page-header>

      @if (!doctorId()) {
        <nz-card><nz-empty nzNotFoundContent="Select a doctor to see and edit their week." /></nz-card>
      } @else if (loading()) {
        <nz-card><nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 8 }" /></nz-card>
      } @else if (loadError()) {
        <nz-card>
          <nz-alert nzType="error" nzShowIcon nzMessage="We could not load this doctor's schedule." [nzAction]="retry" />
          <ng-template #retry><button nz-button nzSize="small" (click)="load()">Try again</button></ng-template>
        </nz-card>
      } @else if (schedule(); as model) {
        <nz-card nzTitle="Weekly hours" [nzExtra]="summary">
          <ng-template #summary><span class="muted num">{{ totalHours() }} h / week</span></ng-template>
          <div class="week" role="table" aria-label="Weekly hours">
            <div class="week__head" role="row">
              <span role="columnheader">Day</span>
              <span role="columnheader">Hours</span>
              <span role="columnheader">Breaks</span>
              <span role="columnheader" class="timeline-scale" aria-hidden="true">
                <span>6 AM</span><span>10 AM</span><span>2 PM</span><span>6 PM</span><span>10 PM</span>
              </span>
              <span role="columnheader"><span class="sr-only">Actions</span></span>
            </div>

            @for (day of model.days; track day.day; let d = $index) {
              <div class="week__row" role="row" [class.is-closed]="!day.open" [class.has-error]="errors().days[day.day]">
                <div class="cell cell--day" role="cell">
                  <nz-switch [ngModel]="day.open" (ngModelChange)="setOpen(d, $event)" nzSize="small"
                             [attr.aria-label]="day.day + ' open'" />
                  <strong>{{ day.day }}</strong>
                </div>

                <div class="cell" role="cell">
                  @if (day.open) {
                    @for (range of day.hours; track $index; let r = $index) {
                      <div class="range">
                        <input nz-input type="time" step="900" [ngModel]="range.start"
                               (ngModelChange)="setTime(d, 'hours', r, 'start', $event)" [attr.aria-label]="day.day + ' start'" />
                        <span class="dash">–</span>
                        <input nz-input type="time" step="900" [ngModel]="range.end"
                               (ngModelChange)="setTime(d, 'hours', r, 'end', $event)" [attr.aria-label]="day.day + ' end'" />
                        @if (day.hours.length > 1) {
                          <button nz-button nzType="text" nzSize="small" (click)="removeRange(d, 'hours', r)"
                                  [attr.aria-label]="'Remove ' + day.day + ' hours'"><span nz-icon nzType="close"></span></button>
                        }
                      </div>
                    }
                    <button nz-button nzType="link" nzSize="small" class="add" (click)="addRange(d, 'hours')">+ Split shift</button>
                  } @else {
                    <span class="muted">Closed</span>
                  }
                </div>

                <div class="cell" role="cell">
                  @if (day.open) {
                    @for (range of day.breaks; track $index; let r = $index) {
                      <div class="range">
                        <input nz-input type="time" step="900" [ngModel]="range.start"
                               (ngModelChange)="setTime(d, 'breaks', r, 'start', $event)" [attr.aria-label]="day.day + ' break start'" />
                        <span class="dash">–</span>
                        <input nz-input type="time" step="900" [ngModel]="range.end"
                               (ngModelChange)="setTime(d, 'breaks', r, 'end', $event)" [attr.aria-label]="day.day + ' break end'" />
                        <button nz-button nzType="text" nzSize="small" (click)="removeRange(d, 'breaks', r)"
                                [attr.aria-label]="'Remove ' + day.day + ' break'"><span nz-icon nzType="close"></span></button>
                      </div>
                    }
                    <button nz-button nzType="link" nzSize="small" class="add" (click)="addRange(d, 'breaks')">+ Break</button>
                  }
                </div>

                <div class="cell cell--timeline" role="cell" aria-hidden="true">
                  <div class="timeline">
                    @if (day.open) {
                      @for (range of day.hours; track $index) {
                        <span class="block block--work" [style.left.%]="pos(range.start)" [style.width.%]="span(range)"></span>
                      }
                      @for (range of day.breaks; track $index) {
                        <span class="block block--break" [style.left.%]="pos(range.start)" [style.width.%]="span(range)"></span>
                      }
                    }
                  </div>
                </div>

                <div class="cell cell--actions" role="cell">
                  @if (day.open) {
                    <button nz-button nzType="text" nzSize="small" nz-dropdown [nzDropdownMenu]="copyMenu"
                            nzTrigger="click" [attr.aria-label]="'Copy ' + day.day + ' to other days'">Copy</button>
                    <nz-dropdown-menu #copyMenu="nzDropdownMenu">
                      <ul nz-menu>
                        <li nz-menu-item (click)="copyTo(d, workWeek)">To Monday–Friday</li>
                        <li nz-menu-item (click)="copyTo(d, allDays)">To every day</li>
                      </ul>
                    </nz-dropdown-menu>
                  }
                </div>

                @if (errors().days[day.day]; as problems) {
                  <div class="row-error" role="alert">{{ problems.join(' ') }}</div>
                }
              </div>
            }
          </div>
        </nz-card>

        <nz-card nzTitle="Time off" [nzExtra]="addTimeOff">
          <ng-template #addTimeOff>
            <button nz-button nzSize="small" (click)="addLeave()"><span nz-icon nzType="plus"></span> Add time off</button>
          </ng-template>
          @for (leave of model.timeOff; track $index; let i = $index) {
            <div class="leave">
              <nz-range-picker [ngModel]="leaveRanges()[i]" (ngModelChange)="setLeaveDates(i, $event)"
                               nzFormat="MMM d, yyyy" [nzAllowClear]="false" aria-label="Dates" />
              <input nz-input class="reason" placeholder="Reason, e.g. Vacation" [ngModel]="leave.reason"
                     (ngModelChange)="setLeaveReason(i, $event)" maxlength="500" aria-label="Reason" />
              <button nz-button nzType="text" (click)="removeLeave(i)" aria-label="Remove time off">
                <span nz-icon nzType="close"></span>
              </button>
              @if (errors().timeOff[i]?.length) { <div class="row-error" role="alert">{{ errors().timeOff[i].join(' ') }}</div> }
            </div>
          } @empty {
            <p class="muted">No time off planned. Appointments can't be booked on days off.</p>
          }
        </nz-card>

        <div class="save-bar" [class.is-dirty]="dirty()">
          <span class="muted">
            @if (errors().count) { {{ errors().count }} problem{{ errors().count === 1 ? '' : 's' }} to fix before saving }
            @else if (dirty()) { You have unsaved changes }
            @else { All changes saved }
          </span>
          <button nz-button (click)="discard()" [disabled]="!dirty() || saving()">Discard</button>
          <button nz-button nzType="primary" (click)="save()" [nzLoading]="saving()" [disabled]="!dirty()">Save schedule</button>
        </div>
      }
    </section>
  `,
  styles: [`
    .doctor-picker { min-width: 240px; }
    .muted { color: var(--da-ink-2); }
    .num { font-variant-numeric: tabular-nums; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    .week { display: grid; grid-template-columns: 170px minmax(250px, 1.2fr) minmax(250px, 1.2fr) minmax(160px, 1fr) 70px; }
    .week__head, .week__row { display: contents; }
    .week__head > span { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .06em;
      color: var(--da-ink-3); padding: 0 var(--da-space-2) var(--da-space-2); }
    .timeline-scale { display: flex !important; justify-content: space-between; text-transform: none !important; letter-spacing: 0 !important; font-weight: 400 !important; }
    .cell { padding: var(--da-space-2); border-top: 1px solid var(--da-border); display: flex; flex-direction: column; gap: var(--da-space-1); min-width: 0; }
    .cell--day { flex-direction: row; align-items: center; gap: var(--da-space-2); }
    .cell--timeline { justify-content: center; }
    .cell--actions { align-items: flex-end; }
    .is-closed .cell--day strong { color: var(--da-ink-3); }
    .range { display: flex; align-items: center; gap: var(--da-space-1); }
    .range input { width: 118px; font-variant-numeric: tabular-nums; }
    .dash { color: var(--da-ink-3); }
    .add { align-self: flex-start; padding: 0; height: auto; }
    .timeline { position: relative; height: 14px; background: var(--da-surface-2); border-radius: 7px; overflow: hidden; }
    .block { position: absolute; top: 0; bottom: 0; border-radius: 7px; }
    .block--work { background: var(--da-brand); opacity: .8; }
    .block--break { background: repeating-linear-gradient(135deg, var(--da-surface) 0 3px, var(--da-surface-2) 3px 6px); }
    .row-error { grid-column: 1 / -1; color: var(--da-danger); font-size: 13px; padding: 0 var(--da-space-2) var(--da-space-2); }
    .leave { display: flex; align-items: center; gap: var(--da-space-2); flex-wrap: wrap; margin-bottom: var(--da-space-2); }
    .leave .reason { flex: 1; min-width: 220px; }
    .leave .row-error { flex-basis: 100%; padding: 0; }
    .save-bar { position: sticky; bottom: 0; z-index: 2; display: flex; justify-content: flex-end; align-items: center; gap: var(--da-space-2);
      padding: var(--da-space-3); background: var(--da-surface); border: 1px solid var(--da-border); border-radius: var(--da-radius); }
    .save-bar > span { margin-right: auto; }
    .save-bar.is-dirty { box-shadow: 0 -4px 16px rgba(0, 0, 0, .06); border-color: var(--da-brand); }
    @media (max-width: 1100px) {
      .week { grid-template-columns: 1fr; }
      .week__head { display: none; }
      .week__row { display: block; border-top: 1px solid var(--da-border); padding: var(--da-space-2) 0; }
      .cell { border-top: 0; padding: var(--da-space-1) 0; }
      .cell--actions { align-items: flex-start; }
    }
    @media (max-width: 640px) { .doctor-picker { width: 100%; } .save-bar { flex-wrap: wrap; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DoctorSchedulesPageComponent {
  private readonly scheduling = inject(SchedulingService);
  private readonly messages = inject(NzMessageService);
  private readonly modal = inject(NzModalService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly workWeek = WORK_WEEK;
  readonly allDays: WeekDay[] = [...WEEK_DAYS];

  readonly doctors = signal<Doctor[]>([]);
  readonly doctorId = signal<string | null>(null);
  readonly schedule = signal<WeeklySchedule | null>(null);
  private readonly saved = signal<string>('');
  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly saving = signal(false);

  readonly errors = computed(() => {
    const schedule = this.schedule();
    return schedule ? validate(schedule) : { days: {}, timeOff: [], count: 0 };
  });
  readonly dirty = computed(() => !!this.schedule() && JSON.stringify(this.schedule()) !== this.saved());
  readonly totalHours = computed(() => {
    const schedule = this.schedule();
    return schedule ? Math.round(weeklyHours(schedule) * 10) / 10 : 0;
  });

  constructor(doctorsService: DoctorsService) {
    doctorsService.getAll().subscribe((doctors) => this.doctors.set(doctors));
    const fromUrl = this.route.snapshot.queryParamMap.get('doctor');
    if (fromUrl) this.open(fromUrl);
  }

  // ---- Doctor ----

  selectDoctor(id: string): void {
    if (id === this.doctorId()) return;
    if (!this.dirty()) {
      this.open(id);
      return;
    }
    const current = this.doctorId();
    this.doctorId.set(id); // keep the select showing the choice while asking
    this.modal.confirm({
      nzTitle: 'Discard unsaved changes?',
      nzContent: 'You changed this doctor’s schedule but haven’t saved it.',
      nzOkText: 'Discard changes',
      nzOkDanger: true,
      nzCancelText: 'Keep editing',
      nzOnOk: () => this.open(id),
      nzOnCancel: () => this.doctorId.set(current)
    });
  }

  private open(id: string): void {
    this.doctorId.set(id);
    this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: { doctor: id } });
    this.load();
  }

  load(): void {
    const id = this.doctorId();
    if (!id) return;
    this.loading.set(true);
    this.loadError.set(false);
    this.scheduling.getAvailability(id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (availability) => this.reset(fromAvailability(availability)),
      error: () => this.loadError.set(true)
    });
  }

  // ---- Weekly hours ----

  setOpen(dayIndex: number, open: boolean): void {
    if (this.schedule()?.days[dayIndex]?.open === open) return;
    this.updateDay(dayIndex, (day) => ({ ...day, open }));
  }

  setTime(dayIndex: number, kind: 'hours' | 'breaks', index: number, edge: keyof TimeRange, value: string): void {
    if (this.schedule()?.days[dayIndex]?.[kind][index]?.[edge] === value) return;
    this.updateDay(dayIndex, (day) => ({
      ...day,
      [kind]: day[kind].map((range, i) => (i === index ? { ...range, [edge]: value } : range))
    }));
  }

  addRange(dayIndex: number, kind: 'hours' | 'breaks'): void {
    this.updateDay(dayIndex, (day) => {
      const last = day.hours[day.hours.length - 1];
      const next: TimeRange = kind === 'breaks'
        ? { ...DEFAULT_BREAK }
        : last ? { start: addHours(last.end, 1), end: addHours(last.end, 4) } : { ...DEFAULT_HOURS };
      return { ...day, [kind]: [...day[kind], next] };
    });
  }

  removeRange(dayIndex: number, kind: 'hours' | 'breaks', index: number): void {
    this.updateDay(dayIndex, (day) => ({ ...day, [kind]: day[kind].filter((_, i) => i !== index) }));
  }

  copyTo(dayIndex: number, targets: WeekDay[]): void {
    const schedule = this.schedule();
    if (!schedule) return;
    const source = schedule.days[dayIndex];
    this.schedule.set({
      ...schedule,
      days: schedule.days.map((day) => targets.includes(day.day) && day.day !== source.day
        ? { ...day, open: true, hours: source.hours.map((h) => ({ ...h })), breaks: source.breaks.map((b) => ({ ...b })) }
        : day)
    });
    this.messages.info(`Copied ${source.day}'s hours to ${targets === this.workWeek ? 'Monday–Friday' : 'every day'}. Save to keep them.`);
  }

  pos(time: string): number {
    return clampPercent(minutes(time));
  }

  span(range: TimeRange): number {
    return Math.max(0, clampPercent(minutes(range.end)) - clampPercent(minutes(range.start)));
  }

  // ---- Time off ----

  addLeave(): void {
    const today = format(new Date(), 'yyyy-MM-dd');
    this.updateTimeOff((list) => [...list, { startDate: today, endDate: today, reason: '' }]);
  }

  removeLeave(index: number): void {
    this.updateTimeOff((list) => list.filter((_, i) => i !== index));
  }

  /**
   * Date pairs for the range pickers. Computed (not a template method) so each picker gets the same array
   * until the dates really change: a fresh array on every check made the picker emit a change, which
   * updated the schedule, which made another fresh array, and froze the page.
   */
  readonly leaveRanges = computed(() =>
    (this.schedule()?.timeOff ?? []).map((l) =>
      l.startDate && l.endDate ? [parseISO(l.startDate), parseISO(l.endDate)] : []));

  setLeaveDates(index: number, range: Date[] | null): void {
    if (!range || range.length !== 2 || !range[0] || !range[1]) return;
    const startDate = format(range[0], 'yyyy-MM-dd');
    const endDate = format(range[1], 'yyyy-MM-dd');
    const current = this.schedule()?.timeOff[index];
    if (!current || (current.startDate === startDate && current.endDate === endDate)) return; // no real change
    this.updateTimeOff((list) => list.map((l, i) => (i === index ? { ...l, startDate, endDate } : l)));
  }

  setLeaveReason(index: number, reason: string): void {
    if (this.schedule()?.timeOff[index]?.reason === reason) return;
    this.updateTimeOff((list) => list.map((l, i) => (i === index ? { ...l, reason } : l)));
  }

  // ---- Save ----

  discard(): void {
    this.schedule.set(JSON.parse(this.saved()));
  }

  save(): void {
    const schedule = this.schedule();
    const id = this.doctorId();
    if (!schedule || !id || this.saving()) return;
    if (this.errors().count) {
      this.messages.warning('Fix the highlighted problems first.');
      return;
    }
    this.saving.set(true);
    this.scheduling.saveAvailability(id, toAvailability(schedule)).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (availability) => {
        // Keep closed days' hours in the editor; take everything else from the server.
        const fresh = fromAvailability(availability);
        fresh.days = fresh.days.map((day, i) => (day.open ? day : { ...schedule.days[i], open: false }));
        this.reset(fresh);
        this.messages.success('Schedule saved.');
      },
      error: (error: unknown) => this.messages.error(saveErrorText(error))
    });
  }

  private reset(schedule: WeeklySchedule): void {
    this.schedule.set(schedule);
    this.saved.set(JSON.stringify(schedule));
  }

  private updateDay(index: number, change: (day: DaySchedule) => DaySchedule): void {
    const schedule = this.schedule();
    if (schedule) this.schedule.set({ ...schedule, days: schedule.days.map((d, i) => (i === index ? change(d) : d)) });
  }

  private updateTimeOff(change: (list: WeeklySchedule['timeOff']) => WeeklySchedule['timeOff']): void {
    const schedule = this.schedule();
    if (schedule) this.schedule.set({ ...schedule, timeOff: change(schedule.timeOff) });
  }
}

function clampPercent(value: number): number {
  const clamped = Math.min(TIMELINE_END, Math.max(TIMELINE_START, value));
  return ((clamped - TIMELINE_START) / (TIMELINE_END - TIMELINE_START)) * 100;
}

function addHours(time: string, hours: number): string {
  const total = Math.min(23 * 60 + 45, minutes(time) + hours * 60);
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}
