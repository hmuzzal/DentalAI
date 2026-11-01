import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { catchError, finalize, map, of } from 'rxjs';
import { describeHttpError } from '../../core/http/error.interceptor';
import { Clinic, ClinicRequest, TimeZoneOption } from '../../core/models/clinic.models';
import { ClinicsService } from '../../core/services/clinics.service';
import { LookupsService } from '../../core/services/lookups.service';

const DEFAULT_ZONE = 'America/New_York';
const DEFAULT_INTERVALS = [5, 10, 15, 20, 30, 60];

/** Add or edit a clinic. Pass `clinic` to edit, or null to create. */
@Component({
  selector: 'app-clinic-form-drawer',
  standalone: true,
  imports: [ReactiveFormsModule, NzAlertModule, NzButtonModule, NzDrawerModule, NzFormModule, NzInputModule, NzSelectModule],
  template: `
    <nz-drawer [nzVisible]="open()" [nzTitle]="title()"
               [nzWidth]="isMobile() ? '100%' : 480" [nzFooter]="footer"
               [nzMaskClosable]="!saving()" (nzOnClose)="closed.emit()">
      <ng-container *nzDrawerContent>
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-name" i18n="@@clinicForm.name">Name</nz-form-label>
            <nz-form-control nzErrorTip="Enter the clinic name (up to 150 characters).">
              <input nz-input id="clinic-name" formControlName="name" autocomplete="organization" />
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-email" i18n="@@clinicForm.email">Email</nz-form-label>
            <nz-form-control nzErrorTip="Enter a valid email address.">
              <input nz-input id="clinic-email" type="email" formControlName="email" autocomplete="email" />
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-phone" i18n="@@clinicForm.phone">Phone</nz-form-label>
            <nz-form-control nzErrorTip="Enter a phone number, e.g. +1 212 555 0100.">
              <input nz-input id="clinic-phone" type="tel" formControlName="phone" autocomplete="tel" />
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-address" i18n="@@clinicForm.address">Address</nz-form-label>
            <nz-form-control nzErrorTip="Enter the clinic address.">
              <textarea nz-input id="clinic-address" rows="2" formControlName="address" autocomplete="street-address"></textarea>
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-zone" i18n="@@clinicForm.timeZone">Time zone</nz-form-label>
            <nz-form-control nzErrorTip="Choose the clinic's time zone."
                             nzExtra="Opening hours, slots and appointment times use this zone.">
              <nz-select nzId="clinic-zone" formControlName="timeZone" nzShowSearch
                         [nzLoading]="zones() === null" nzPlaceHolder="Search time zones">
                @for (group of zoneGroups(); track group.name) {
                  <nz-option-group [nzLabel]="group.name">
                    @for (zone of group.zones; track zone.id) {
                      <nz-option [nzValue]="zone.id" [nzLabel]="zone.label + ' · ' + zone.id" />
                    }
                  </nz-option-group>
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-interval" i18n="@@clinicForm.slotInterval">Slot interval</nz-form-label>
            <nz-form-control nzExtra="How often a new appointment can start, e.g. every 15 minutes.">
              <nz-select nzId="clinic-interval" formControlName="slotIntervalMinutes">
                @for (minutes of intervals(); track minutes) {
                  <nz-option [nzValue]="minutes" [nzLabel]="'Every ' + minutes + ' minutes'" />
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>

          @if (serverError()) {
            <nz-alert nzType="error" nzShowIcon [nzMessage]="serverError()!" />
          }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-drawer>

    <ng-template #footer>
      <div class="footer">
        <button nz-button (click)="closed.emit()" [disabled]="saving()" i18n="@@common.cancel">Cancel</button>
        <button nz-button nzType="primary" (click)="save()" [nzLoading]="saving()">
          {{ submitLabel() }}
        </button>
      </div>
    </ng-template>
  `,
  styles: [`.footer { display: flex; justify-content: flex-end; gap: var(--da-space-2); }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClinicFormDrawerComponent {
  readonly open = input(false);
  readonly clinic = input<Clinic | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Clinic>();

  private readonly clinicsService = inject(ClinicsService);
  private readonly lookups = inject(LookupsService);
  private readonly messages = inject(NzMessageService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly isMobile = toSignal(inject(BreakpointObserver).observe('(max-width: 640px)').pipe(map((x) => x.matches)), {
    initialValue: false
  });

  /** null while loading; an empty list if the lookup failed. */
  readonly zones = toSignal(this.lookups.timeZones().pipe(catchError(() => of([] as TimeZoneOption[]))), {
    initialValue: null
  });
  readonly intervals = toSignal(this.lookups.slotIntervals().pipe(catchError(() => of(DEFAULT_INTERVALS))), {
    initialValue: DEFAULT_INTERVALS
  });

  readonly zoneGroups = computed(() => {
    const zones = [...(this.zones() ?? [])];
    const current = this.clinic()?.timeZone;
    // Keep a clinic's existing zone selectable even if this server's list does not include it.
    if (current && !zones.some((z) => z.id === current)) {
      zones.unshift({ id: current, label: current, utcOffsetMinutes: 0, group: 'Current' });
    }
    const names = [...new Set(zones.map((z) => z.group))];
    return names.map((name) => ({ name, zones: zones.filter((z) => z.group === name) }));
  });

  readonly title = computed(() =>
    this.clinic() ? $localize`:@@clinicForm.editTitle:Edit clinic` : $localize`:@@clinicForm.newTitle:New clinic`);
  readonly submitLabel = computed(() =>
    this.clinic() ? $localize`:@@clinicForm.save:Save changes` : $localize`:@@clinicForm.create:Create clinic`);

  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9\s\-().]{7,20}$/)]],
    address: ['', [Validators.required, Validators.maxLength(500)]],
    timeZone: [DEFAULT_ZONE, Validators.required],
    slotIntervalMinutes: [15, Validators.required]
  });

  constructor() {
    // Reset the form each time the drawer opens.
    effect(() => {
      if (!this.open()) return;
      const clinic = this.clinic();
      this.serverError.set(null);
      this.form.reset({
        name: clinic?.name ?? '',
        email: clinic?.email ?? '',
        phone: clinic?.phone ?? '',
        address: clinic?.address ?? '',
        timeZone: clinic?.timeZone || browserZone() || DEFAULT_ZONE,
        slotIntervalMinutes: clinic?.slotIntervalMinutes ?? 15
      });
    });
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      Object.values(this.form.controls).forEach((control) => {
        control.markAsDirty();
        control.updateValueAndValidity({ onlySelf: true });
      });
      return;
    }

    const request: ClinicRequest = this.form.getRawValue();
    const clinic = this.clinic();
    const call = clinic ? this.clinicsService.update(clinic.id, request) : this.clinicsService.create(request);

    this.saving.set(true);
    this.serverError.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result) => {
        this.messages.success(clinic ? $localize`:@@clinicForm.updated:Clinic updated.` : $localize`:@@clinicForm.created:Clinic created.`);
        this.saved.emit(result);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse) {
          const errors: string[] | undefined = error.error?.errors;
          this.serverError.set(errors?.length ? errors.join(' ') : describeHttpError(error));
        }
      }
    });
  }
}

/** The browser's IANA zone, used as the default for a new clinic. */
function browserZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
  } catch {
    return null;
  }
}
