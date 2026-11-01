import { HttpErrorResponse } from '@angular/common/http';
import { BreakpointObserver } from '@angular/cdk/layout';

import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { format, parseISO } from 'date-fns';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { finalize, forkJoin, map } from 'rxjs';
import { describeHttpError } from '../../core/http/error.interceptor';
import { Doctor } from '../../core/models/doctor.models';
import { Patient } from '../../core/models/patient.models';
import { AvailableSlot } from '../../core/models/scheduling.models';
import { DentalService } from '../../core/models/service.models';
import { AppointmentsService } from '../../core/services/appointments.service';
import { AuthStateService } from '../../core/services/auth-state.service';
import { BookingService } from '../../core/services/booking.service';
import { BillingSettingsService } from '../../core/services/billing-settings.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { DoctorsService } from '../../core/services/doctors.service';
import { PatientsService } from '../../core/services/patients.service';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';
import { dateKeyInZone, effectiveTimeZone, formatInZone, timeZoneLabel } from '../../core/time-zone';
import { TimeSlotSelectorComponent } from '../../shared/time-slot-selector.component';

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

/** App-wide "New appointment" drawer. Hosted once by the app shell; opened via BookingService. */
@Component({
  selector: 'app-booking-drawer',
  standalone: true,
  imports: [
    MoneyPipe, FormsModule, NzAlertModule, NzButtonModule, NzDatePickerModule, NzDrawerModule, NzFormModule,
    NzInputModule, NzSelectModule, NzSkeletonModule, TimeSlotSelectorComponent
  ],
  template: `
    <nz-drawer [nzVisible]="booking.isOpen()" [nzTitle]="rescheduling() ? 'Reschedule appointment' : 'New appointment'"
               [nzWidth]="isMobile() ? '100%' : 480"
               [nzFooter]="footer" [nzMaskClosable]="!submitting()" (nzOnClose)="close()">
      <ng-container *nzDrawerContent>
        @switch (loadState()) {
          @case ('loading') { <nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 8 }" /> }
          @case ('error') {
            <nz-alert nzType="error" nzShowIcon nzMessage="We could not load doctors, services and patients."
                      [nzAction]="retryTpl" />
            <ng-template #retryTpl><button nz-button nzSize="small" (click)="loadOptions()">Try again</button></ng-template>
          }
          @case ('ready') {
            @if (!doctors().length) {
              <div class="empty">
                <p>Add a doctor before booking appointments.</p>
                <button nz-button nzType="primary" (click)="goTo('/doctors')">Go to Doctors</button>
              </div>
            } @else {
              <form nz-form nzLayout="vertical" class="form" (ngSubmit)="submit()">
                @if (rescheduling(); as current) {
                  <!-- Doctor, service and patient stay the same; only the time moves. -->
                  <dl class="current">
                    <div><dt>Patient</dt><dd>{{ current.patientName }}</dd></div>
                    <div><dt>Doctor</dt><dd>{{ current.doctorName }}</dd></div>
                    <div><dt>Service</dt><dd>{{ current.serviceName }}</dd></div>
                    <div><dt>Currently</dt><dd>{{ currentTimeLabel() }}</dd></div>
                  </dl>
                } @else {
                <nz-form-item>
                  <nz-form-label nzFor="booking-doctor" nzRequired>Doctor</nz-form-label>
                  <nz-form-control>
                    <nz-select nzId="booking-doctor" name="doctor" nzShowSearch nzPlaceHolder="Select a doctor"
                               [ngModel]="doctorId()" (ngModelChange)="selectDoctor($event)">
                      @for (doctor of doctors(); track doctor.id) {
                        <nz-option [nzValue]="doctor.id" [nzLabel]="doctorName(doctor)" />
                      }
                    </nz-select>
                  </nz-form-control>
                </nz-form-item>

                <nz-form-item>
                  <nz-form-label nzFor="booking-service" nzRequired>Service</nz-form-label>
                  <nz-form-control [nzExtra]="selectedService() ? serviceExtra : undefined">
                    <nz-select nzId="booking-service" name="service" nzShowSearch nzPlaceHolder="Select a service"
                               [nzDisabled]="!doctorId()" [nzNotFoundContent]="'No active services for this clinic'"
                               [ngModel]="serviceId()" (ngModelChange)="serviceId.set($event); slot.set(null)">
                      @for (service of services(); track service.id) {
                        <nz-option [nzValue]="service.id" [nzLabel]="service.name + ' · ' + service.durationMinutes + ' min'" />
                      }
                    </nz-select>
                    <ng-template #serviceExtra>
                      {{ selectedService()!.durationMinutes }} minutes · {{ selectedService()!.price | money: billing.currency() }}
                    </ng-template>
                  </nz-form-control>
                </nz-form-item>

                <nz-form-item>
                  <nz-form-label nzFor="booking-patient" nzRequired>Patient</nz-form-label>
                  <nz-form-control [nzExtra]="doctorId() && !patients().length ? noPatients : undefined">
                    <nz-select nzId="booking-patient" name="patient" nzShowSearch nzPlaceHolder="Search by name or phone"
                               [nzDisabled]="!doctorId()" [nzFilterOption]="filterPatient"
                               [nzNotFoundContent]="'No matching patients'"
                               [ngModel]="patientId()" (ngModelChange)="patientId.set($event)">
                      @for (patient of patients(); track patient.id) {
                        <nz-option [nzValue]="patient.id" [nzLabel]="patientLabel(patient)" />
                      }
                    </nz-select>
                    <ng-template #noPatients>
                      This clinic has no patients yet.
                      <button nz-button nzType="link" type="button" class="inline-link" (click)="goTo('/patients')">Add one</button>
                    </ng-template>
                  </nz-form-control>
                </nz-form-item>
                }

                <nz-form-item>
                  <nz-form-label nzFor="booking-date" nzRequired>Date</nz-form-label>
                  <nz-form-control>
                    <nz-date-picker nzId="booking-date" name="date" class="full" nzFormat="EEE, MMM d, yyyy"
                                    [nzAllowClear]="false" [nzDisabledDate]="isPastDate"
                                    [ngModel]="date()" (ngModelChange)="date.set($event); slot.set(null)" />
                  </nz-form-control>
                </nz-form-item>

                <nz-form-item>
                  <nz-form-label nzRequired>Time</nz-form-label>
                  <nz-form-control [nzExtra]="zoneLabel() ? 'Times are in ' + zoneLabel() + '.' : undefined">
                    <!-- Shown beside the slots: most failures mean "pick another time". -->
                    @if (error()) {
                      <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="form__error" role="alert" />
                    }
                    <app-time-slot-selector [doctorId]="doctorId()" [serviceId]="serviceId()" [date]="dateKey()"
                                            [timeZone]="timeZone()" [refresh]="slotRefresh()"
                                            [excludeAppointmentId]="rescheduling()?.id ?? null"
                                            [(selected)]="slot" (selectedChange)="error.set(null)" />
                  </nz-form-control>
                </nz-form-item>

                @if (!rescheduling()) {
                  <nz-form-item>
                    <nz-form-label nzFor="booking-notes">Notes</nz-form-label>
                    <nz-form-control>
                      <textarea nz-input id="booking-notes" name="notes" rows="3" maxlength="2000"
                                placeholder="Reason for visit, anything the doctor should know"
                                [ngModel]="notes()" (ngModelChange)="notes.set($event)"></textarea>
                    </nz-form-control>
                  </nz-form-item>
                }
              </form>
            }
          }
        }
      </ng-container>
    </nz-drawer>

    <ng-template #footer>
      <div class="footer">
        <div class="footer__summary" aria-live="polite">
          @if (summary(); as text) { <strong>{{ text }}</strong> } @else { <span>Pick a time to continue.</span> }
        </div>
        <div class="footer__actions">
          <button nz-button type="button" [disabled]="submitting()" (click)="close()">Cancel</button>
          <button nz-button nzType="primary" type="button" [disabled]="!canSubmit()" [nzLoading]="submitting()"
                  (click)="submit()">{{ rescheduling() ? 'Move appointment' : 'Book appointment' }}</button>
        </div>
      </div>
    </ng-template>
  `,
  styles: [`
    .form nz-form-item { margin-bottom: var(--da-space-4); }
    .full { width: 100%; }
    .inline-link { padding: 0 4px; height: auto; }
    .form__error { margin-bottom: var(--da-space-3); }
    .current { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); margin: 0 0 var(--da-space-4);
      padding: var(--da-space-3); background: var(--da-surface-2); border-radius: var(--da-radius); }
    .current dt { font-size: 12px; color: var(--da-ink-3); }
    .current dd { margin: 0; color: var(--da-ink); font-weight: 500; }
    .empty { display: grid; justify-items: start; gap: var(--da-space-3); color: var(--da-ink-2); }
    .empty p { margin: 0; }
    .footer { display: flex; align-items: center; justify-content: space-between; gap: var(--da-space-3); flex-wrap: wrap; }
    .footer__summary { min-width: 0; color: var(--da-ink-3); font-size: 13px; }
    .footer__summary strong { color: var(--da-ink); font-weight: 600; }
    .footer__actions { display: flex; gap: var(--da-space-2); margin-left: auto; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BookingDrawerComponent {
  readonly booking = inject(BookingService);
  private readonly authState = inject(AuthStateService);
  private readonly appointments = inject(AppointmentsService);
  private readonly doctorsService = inject(DoctorsService);
  private readonly servicesService = inject(ServicesCatalogService);
  private readonly patientsService = inject(PatientsService);
  private readonly messages = inject(NzMessageService);
  readonly billing = inject(BillingSettingsService);
  private readonly router = inject(Router);

  readonly isMobile = toSignal(
    inject(BreakpointObserver).observe('(max-width: 640px)').pipe(map((state) => state.matches)),
    { initialValue: false }
  );

  readonly loadState = signal<LoadState>('idle');
  private readonly allDoctors = signal<Doctor[]>([]);
  private readonly allServices = signal<DentalService[]>([]);
  private readonly allPatients = signal<Patient[]>([]);

  readonly doctorId = signal<string | null>(null);
  readonly serviceId = signal<string | null>(null);
  readonly patientId = signal<string | null>(null);
  readonly date = signal<Date | null>(null);
  readonly slot = signal<AvailableSlot | null>(null);
  readonly notes = signal('');
  readonly slotRefresh = signal(0);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  /** The appointment being moved, or null when booking a new one. */
  readonly rescheduling = computed(() => {
    const mode = this.booking.mode();
    return mode.kind === 'reschedule' ? mode.appointment : null;
  });

  readonly doctors = this.allDoctors.asReadonly();
  private readonly selectedDoctor = computed(() => this.allDoctors().find((doctor) => doctor.id === this.doctorId()) ?? null);

  // Slots are in the selected doctor's clinic zone. That matters for platform admins, who can book for
  // any clinic; until a doctor is chosen, fall back to the user's own clinic.
  readonly timeZone = computed(() =>
    effectiveTimeZone(this.selectedDoctor()?.clinicTimeZone ?? this.authState.user()?.clinicTimeZone));
  readonly zoneLabel = computed(() => timeZoneLabel(this.timeZone()));
  private readonly todayKey = computed(() => dateKeyInZone(new Date(), this.timeZone()));

  readonly currentTimeLabel = computed(() => {
    const current = this.rescheduling();
    return current
      ? formatInZone(current.startTime, this.timeZone(), { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
      : '';
  });
  readonly services = computed(() => {
    const clinicId = this.selectedDoctor()?.clinicId;
    return this.allServices().filter((service) => service.isActive && service.clinicId === clinicId);
  });
  readonly patients = computed(() => {
    const clinicId = this.selectedDoctor()?.clinicId;
    return this.allPatients().filter((patient) => patient.clinicId === clinicId);
  });
  readonly selectedService = computed(() => this.services().find((service) => service.id === this.serviceId()) ?? null);
  readonly dateKey = computed(() => (this.date() ? format(this.date()!, 'yyyy-MM-dd') : null));

  readonly canSubmit = computed(() =>
    !this.submitting() && !!this.slot()
    && (!!this.rescheduling() || !!(this.doctorId() && this.serviceId() && this.patientId())));

  readonly summary = computed(() => {
    const slot = this.slot();
    if (!slot) return null;
    const zone = this.timeZone();
    const day = formatInZone(slot.startTimeUtc, zone, { weekday: 'short', month: 'short', day: 'numeric' });
    const start = formatInZone(slot.startTimeUtc, zone, { hour: 'numeric', minute: '2-digit' });
    const end = formatInZone(slot.endTimeUtc, zone, { hour: 'numeric', minute: '2-digit' });
    return `${day} · ${start} – ${end}`;
  });

  /** nz-date-picker passes local Dates for calendar days; compare by calendar date. */
  readonly isPastDate = (value: Date): boolean => format(value, 'yyyy-MM-dd') < this.todayKey();

  /** Match patients by name or phone. */
  readonly filterPatient = (input: string, option: { nzLabel?: unknown }): boolean =>
    String(option.nzLabel ?? '').toLowerCase().includes(input.trim().toLowerCase());

  constructor() {
    // Each time the drawer opens: reset the form from the requested defaults and refresh the lists.
    effect(() => {
      if (!this.booking.isOpen()) return;
      untracked(() => {
        this.reset();
        this.loadOptions();
      });
    });
  }

  loadOptions(): void {
    this.loadState.set('loading');
    forkJoin({
      doctors: this.doctorsService.getAll(),
      services: this.servicesService.getAll(),
      patients: this.patientsService.getAll()
    }).subscribe({
      next: ({ doctors, services, patients }) => {
        this.allDoctors.set([...doctors].sort((a, b) => this.doctorName(a).localeCompare(this.doctorName(b))));
        this.allServices.set([...services].sort((a, b) => a.name.localeCompare(b.name)));
        this.allPatients.set([...patients].sort((a, b) => a.fullName.localeCompare(b.fullName)));
        this.applyDoctorDefault();
        // The doctor (and so the time zone) is known now: settle the starting date in that zone.
        this.applyDateDefault();
        this.loadState.set('ready');
      },
      error: () => this.loadState.set('error')
    });
  }

  selectDoctor(doctorId: string | null): void {
    this.doctorId.set(doctorId);
    this.slot.set(null);
    // Services and patients are clinic-scoped; drop selections that no longer fit.
    if (!this.services().some((service) => service.id === this.serviceId())) this.serviceId.set(null);
    if (!this.patients().some((patient) => patient.id === this.patientId())) this.patientId.set(null);
    // Another clinic may be in another zone, where the chosen date could already be past.
    const dateKey = this.dateKey();
    if (dateKey && dateKey < this.todayKey()) this.date.set(parseISO(this.todayKey()));
  }

  submit(): void {
    const slot = this.slot();
    if (!this.canSubmit() || !slot) return;

    this.submitting.set(true);
    this.error.set(null);
    const current = this.rescheduling();
    const request$ = current
      ? this.appointments.reschedule(current.id, { startTime: slot.startTimeUtc })
      : this.appointments.create({
          doctorId: this.doctorId()!,
          serviceId: this.serviceId()!,
          patientId: this.patientId()!,
          startTime: slot.startTimeUtc,
          notes: this.notes().trim() || null
        });

    request$
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (appointment) => {
          this.messages.success(current
            ? `Moved ${appointment.patientName} to ${this.summary()}.`
            : `Booked ${appointment.patientName} for ${this.summary()}.`);
          this.booking.notifyChanged(appointment);
          this.booking.close();
        },
        error: (error: unknown) => {
          const httpError = error instanceof HttpErrorResponse ? error : null;
          this.error.set(httpError
            ? describeHttpError(httpError)
            : current ? 'The appointment could not be moved.' : 'The appointment could not be booked.');
          // The slot was probably taken meanwhile: refresh the list and make them pick again.
          if (httpError && (httpError.status === 409 || httpError.status === 400)) {
            this.slot.set(null);
            this.slotRefresh.update((value) => value + 1);
          }
        }
      });
  }

  close(): void {
    if (!this.submitting()) this.booking.close();
  }

  goTo(path: string): void {
    this.booking.close();
    void this.router.navigateByUrl(path);
  }

  doctorName(doctor: Doctor): string {
    return `${doctor.firstName} ${doctor.lastName}`.trim();
  }

  patientLabel(patient: Patient): string {
    return patient.phone ? `${patient.fullName} · ${patient.phone}` : patient.fullName;
  }

  private reset(): void {
    const mode = this.booking.mode();
    if (mode.kind === 'reschedule') {
      this.doctorId.set(mode.appointment.doctorId);
      this.serviceId.set(mode.appointment.serviceId);
    } else {
      this.doctorId.set(mode.defaults.doctorId ?? null);
      this.serviceId.set(null);
    }
    this.patientId.set(null);
    this.date.set(null);
    this.slot.set(null);
    this.notes.set('');
    this.error.set(null);
  }

  /** Keep a requested doctor only if it exists; preselect when the clinic has exactly one. */
  private applyDoctorDefault(): void {
    if (this.rescheduling()) return;
    const doctors = this.allDoctors();
    if (!doctors.some((doctor) => doctor.id === this.doctorId())) {
      this.doctorId.set(doctors.length === 1 ? doctors[0].id : null);
    }
  }

  /**
   * Starting date, in the doctor's clinic zone: the appointment's current day when rescheduling,
   * otherwise the requested day; never a past day.
   */
  private applyDateDefault(): void {
    const mode = this.booking.mode();
    const today = this.todayKey();
    const requested = mode.kind === 'reschedule'
      ? dateKeyInZone(mode.appointment.startTime, this.timeZone())
      : mode.defaults.date;
    this.date.set(parseISO(requested && requested >= today ? requested : today));
  }
}
