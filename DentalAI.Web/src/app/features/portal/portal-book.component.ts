import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { finalize, forkJoin, of } from 'rxjs';
import { PortalAppointment, PortalDoctor, PortalService } from '../../core/models/portal.models';
import { AvailableSlot } from '../../core/models/scheduling.models';
import { PortalApiService } from '../../core/services/portal.service';
import { dateKeyInZone, formatInZone } from '../../core/time-zone';
import { optional, saveErrorText } from '../../shared/forms';
import { PageStateComponent } from '../../shared/page-state.component';
import { LoadStatus } from '../../shared/remote-data';
import { SlotLoader, TimeSlotSelectorComponent } from '../../shared/time-slot-selector.component';
import { PortalContext } from './portal-context';

const DAYS_PER_PAGE = 7;

interface Day {
  key: string;
  weekday: string;
  day: string;
  month: string;
}

/**
 * /p/:slug/book — service, doctor, day, time, confirm. Also /p/:slug/appointments/:id/reschedule, where the
 * service and doctor are fixed and only the time changes. Open times come from the same engine as the front desk.
 */
@Component({
  selector: 'app-portal-book',
  standalone: true,
  imports: [
    FormsModule, RouterLink, NzAlertModule, NzButtonModule, NzCardModule, NzIconModule, NzInputModule, NzRadioModule,
    PageStateComponent, TimeSlotSelectorComponent
  ],
  template: `
    <a nz-button nzType="link" class="back" [routerLink]="context.link()"><span nz-icon nzType="left"></span>My visits</a>
    <h1>{{ rescheduling() ? 'Change your appointment' : 'Book a visit' }}</h1>

    <app-page-state [status]="loadStatus()" (retry)="load()" errorText="We could not load booking options."
                    [empty]="!rescheduling() && !services().length" emptyText="No services can be booked online right now. Please call the clinic.">
      @if (rescheduling(); as current) {
        <nz-card class="step">
          <p class="muted">Currently</p>
          <strong>{{ context.when(current.startTime) }}</strong>
          <div>{{ current.serviceName }} with {{ current.doctorName }}</div>
        </nz-card>
      } @else {
        <nz-card class="step" nzTitle="1. What do you need?">
          <nz-radio-group [ngModel]="serviceId()" (ngModelChange)="pickService($event)" class="options" aria-label="Service">
            @for (service of services(); track service.id) {
              <label nz-radio [nzValue]="service.id" class="option">
                <span><strong>{{ service.name }}</strong> <span class="muted">· {{ service.durationMinutes }} min</span>
                  @if (service.description) { <br /><small class="muted">{{ service.description }}</small> }</span>
              </label>
            }
          </nz-radio-group>
        </nz-card>

        <nz-card class="step" nzTitle="2. Who would you like to see?">
          <nz-radio-group [ngModel]="doctorId()" (ngModelChange)="pickDoctor($event)" class="options" aria-label="Doctor">
            @for (doctor of doctors(); track doctor.id) {
              <label nz-radio [nzValue]="doctor.id" class="option">
                <span><strong>{{ doctor.name }}</strong>
                  @if (doctor.specialty) { <span class="muted"> · {{ doctor.specialty }}</span> }</span>
              </label>
            }
          </nz-radio-group>
        </nz-card>
      }

      <nz-card class="step" [nzTitle]="rescheduling() ? '1. Choose a new day' : '3. Choose a day'">
        <div class="days" role="group" aria-label="Day">
          <button nz-button nzShape="circle" class="days__nav" (click)="page.set(page() - 1)" [disabled]="page() === 0" aria-label="Earlier days">
            <span nz-icon nzType="left"></span>
          </button>
          <div class="days__list">
            @for (day of visibleDays(); track day.key) {
              <button type="button" class="day" [class.day--on]="day.key === date()" (click)="pickDate(day.key)" [attr.aria-pressed]="day.key === date()">
                <small>{{ day.weekday }}</small><strong>{{ day.day }}</strong><small>{{ day.month }}</small>
              </button>
            }
          </div>
          <button nz-button nzShape="circle" class="days__nav" (click)="page.set(page() + 1)" [disabled]="!hasMoreDays()" aria-label="Later days">
            <span nz-icon nzType="right"></span>
          </button>
        </div>
      </nz-card>

      <nz-card class="step" [nzTitle]="rescheduling() ? '2. Choose a time' : '4. Choose a time'">
        <app-time-slot-selector [doctorId]="effectiveDoctorId()" [serviceId]="effectiveServiceId()" [date]="date()"
                                [timeZone]="context.zone()" [excludeAppointmentId]="rescheduling()?.id ?? null"
                                [loader]="loader" [refresh]="refresh()" [(selected)]="slot" />
      </nz-card>

      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="step" /> }
      @if (slot()) {
        <nz-card class="step confirm">
          <p class="muted">{{ rescheduling() ? 'New time' : 'Your visit' }}</p>
          <strong class="confirm__when">{{ summary() }}</strong>
          <div>{{ selectedServiceName() }} with {{ selectedDoctorName() }}</div>
          @if (!rescheduling()) {
            <label class="label" for="note">Anything the clinic should know? (optional)</label>
            <textarea nz-input id="note" rows="2" maxlength="500" [(ngModel)]="note" placeholder="e.g. a sensitive tooth on the lower left"></textarea>
          }
          <button nz-button nzType="primary" nzSize="large" nzBlock class="gap" [nzLoading]="submitting()" (click)="submit()">
            {{ rescheduling() ? 'Move my appointment' : 'Book this time' }}
          </button>
          @if (!rescheduling()) {
            <p class="muted small">Cancel or change online until {{ context.clinic()?.cancelCutoffHours }} hours before your visit.</p>
          }
        </nz-card>
      }
    </app-page-state>
  `,
  styles: [`
    .back { padding-left: 0; }
    h1 { font-size: 24px; margin: 0 0 var(--da-space-4); }
    .step + .step { margin-top: var(--da-space-3); }
    .options { display: grid; gap: var(--da-space-2); width: 100%; }
    .option { display: flex; align-items: flex-start; padding: var(--da-space-3); border: 1px solid var(--da-border);
      border-radius: var(--da-radius); margin: 0; white-space: normal; }
    .option.ant-radio-wrapper-checked { border-color: var(--da-brand); background: var(--da-brand-soft); }
    .muted { color: var(--da-ink-3); margin: 0; }
    .small { font-size: 13px; margin-top: var(--da-space-2); }
    .days { display: flex; align-items: center; gap: var(--da-space-2); }
    .days__list { flex: 1; display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: var(--da-space-1); }
    .days__nav { flex: none; }
    .day { display: grid; justify-items: center; gap: 2px; padding: var(--da-space-2) 0; border: 1px solid var(--da-border);
      border-radius: var(--da-radius); background: var(--da-surface); cursor: pointer; color: var(--da-ink); font: inherit; }
    .day small { font-size: 11px; color: var(--da-ink-3); text-transform: uppercase; }
    .day strong { font-size: 16px; font-variant-numeric: tabular-nums; }
    .day:hover { border-color: var(--da-brand); }
    .day--on { border-color: var(--da-brand); background: var(--da-brand); color: #fff; }
    .day--on small { color: rgba(255, 255, 255, .85); }
    .confirm__when { font-size: 18px; display: block; }
    .label { display: block; font-weight: 500; margin: var(--da-space-3) 0 var(--da-space-1); }
    .gap { margin-top: var(--da-space-3); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalBookComponent {
  readonly context = inject(PortalContext);
  private readonly api = inject(PortalApiService);
  private readonly router = inject(Router);
  private readonly messages = inject(NzMessageService);
  private readonly rescheduleId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  readonly loadStatus = signal<LoadStatus>('loading');
  readonly services = signal<PortalService[]>([]);
  readonly doctors = signal<PortalDoctor[]>([]);
  readonly rescheduling = signal<PortalAppointment | null>(null);

  readonly serviceId = signal<string | null>(null);
  readonly doctorId = signal<string | null>(null);
  readonly date = signal<string | null>(null);
  readonly slot = signal<AvailableSlot | null>(null);
  readonly note = signal('');
  readonly page = signal(0);
  readonly refresh = signal(0);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly loader: SlotLoader = (doctorId, date, serviceId, exclude) => this.api.slots(doctorId, date, serviceId, exclude);

  readonly effectiveServiceId = computed(() => this.rescheduling()?.serviceId ?? this.serviceId());
  readonly effectiveDoctorId = computed(() => this.rescheduling()?.doctorId ?? this.doctorId());

  /** Today to the clinic's booking horizon, in the clinic's zone. */
  private readonly days = computed<Day[]>(() => {
    const clinic = this.context.clinic();
    const todayKey = dateKeyInZone(new Date(), this.context.zone());
    const count = Math.max(1, (clinic?.maxDaysAhead ?? 30) + 1);
    const start = new Date(`${todayKey}T12:00:00Z`);
    return Array.from({ length: count }, (_, i) => {
      const d = new Date(start.getTime() + i * 86_400_000);
      return {
        key: d.toISOString().slice(0, 10),
        weekday: formatInZone(d, 'UTC', { weekday: 'short' }),
        day: formatInZone(d, 'UTC', { day: 'numeric' }),
        month: formatInZone(d, 'UTC', { month: 'short' })
      };
    });
  });
  readonly visibleDays = computed(() => this.days().slice(this.page() * DAYS_PER_PAGE, (this.page() + 1) * DAYS_PER_PAGE));
  readonly hasMoreDays = computed(() => (this.page() + 1) * DAYS_PER_PAGE < this.days().length);

  readonly selectedServiceName = computed(() =>
    this.rescheduling()?.serviceName ?? this.services().find((s) => s.id === this.serviceId())?.name ?? '');
  readonly selectedDoctorName = computed(() =>
    this.rescheduling()?.doctorName ?? this.doctors().find((d) => d.id === this.doctorId())?.name ?? '');
  readonly summary = computed(() => {
    const slot = this.slot();
    if (!slot) return '';
    const zone = this.context.zone();
    return `${formatInZone(slot.startTimeUtc, zone, { weekday: 'long', month: 'long', day: 'numeric' })}, `
      + `${formatInZone(slot.startTimeUtc, zone, { hour: 'numeric', minute: '2-digit' })} – `
      + `${formatInZone(slot.endTimeUtc, zone, { hour: 'numeric', minute: '2-digit' })}`;
  });

  constructor() {
    this.load();
    // A different service, doctor or day means a new choice of time.
    effect(() => {
      this.effectiveServiceId();
      this.effectiveDoctorId();
      this.date();
      untracked(() => {
        this.slot.set(null);
        this.error.set(null);
      });
    });
  }

  load(): void {
    this.loadStatus.set('loading');
    forkJoin({
      services: this.rescheduleId ? of([] as PortalService[]) : this.api.services(),
      doctors: this.rescheduleId ? of([] as PortalDoctor[]) : this.api.doctors(),
      appointments: this.rescheduleId ? this.api.appointments() : of([] as PortalAppointment[])
    }).subscribe({
      next: ({ services, doctors, appointments }) => {
        this.services.set(services);
        this.doctors.set(doctors);
        if (services.length === 1) this.serviceId.set(services[0].id);
        if (doctors.length === 1) this.doctorId.set(doctors[0].id);
        if (this.rescheduleId) {
          const current = appointments.find((a) => a.id === this.rescheduleId && a.canReschedule) ?? null;
          if (!current) {
            this.messages.warning('This appointment can no longer be changed online.');
            void this.router.navigate(this.context.link());
            return;
          }
          this.rescheduling.set(current);
        }
        this.date.set(this.days()[0]?.key ?? null);
        this.loadStatus.set('ready');
      },
      error: () => this.loadStatus.set('error')
    });
  }

  pickService(id: string): void { this.serviceId.set(id); }
  pickDoctor(id: string): void { this.doctorId.set(id); }
  pickDate(key: string): void { this.date.set(key); }

  submit(): void {
    const slot = this.slot();
    if (!slot || this.submitting()) return;
    const current = this.rescheduling();
    const call = current
      ? this.api.reschedule(current.id, slot.startTimeUtc)
      : this.api.book({ doctorId: this.doctorId()!, serviceId: this.serviceId()!, startTime: slot.startTimeUtc, note: optional(this.note()) });

    this.submitting.set(true);
    this.error.set(null);
    call.pipe(finalize(() => this.submitting.set(false))).subscribe({
      next: (response) => {
        this.messages.success(response.message);
        void this.router.navigate(this.context.link());
      },
      error: (error: unknown) => {
        this.error.set(saveErrorText(error));
        // Someone else took the time: show what's still open.
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.slot.set(null);
          this.refresh.update((n) => n + 1);
        }
      }
    });
  }
}
