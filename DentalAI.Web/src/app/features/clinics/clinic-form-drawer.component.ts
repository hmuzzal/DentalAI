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
import { Clinic, ClinicRequest, CountryOption, TimeZoneOption } from '../../core/models/clinic.models';
import { formatPhone } from '../../shared/phone.pipe';
import { ClinicsService } from '../../core/services/clinics.service';
import { StaffService } from '../../core/services/staff.service';
import { StaffInvite } from '../../core/models/staff.models';
import { saveErrorText, touchAll } from '../../shared/forms';
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
            <nz-form-label nzRequired nzFor="clinic-country" i18n="@@clinicForm.country">Country</nz-form-label>
            <nz-form-control nzErrorTip="Choose the clinic's country."
                             nzExtra="Phone numbers typed without a country code are read as this country's.">
              <nz-select nzId="clinic-country" formControlName="country" nzShowSearch
                         [nzLoading]="!countries().length" nzPlaceHolder="Search countries">
                @for (country of countries(); track country.code) {
                  <nz-option [nzValue]="country.code" [nzLabel]="country.name" />
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-label nzRequired nzFor="clinic-phone" i18n="@@clinicForm.phone">Phone</nz-form-label>
            <nz-form-control nzErrorTip="Enter a phone number, e.g. (212) 555-0142.">
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

          @if (!clinic()) {
            <fieldset [formGroup]="adminForm">
              <legend>First admin</legend>
              <p class="hint">They get an invite link to set their own password, then invite the rest of their team.</p>
              <div class="row">
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="admin-first">First name</nz-form-label>
                  <nz-form-control nzErrorTip="Enter a first name.">
                    <input nz-input id="admin-first" formControlName="firstName" autocomplete="off" />
                  </nz-form-control>
                </nz-form-item>
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="admin-last">Last name</nz-form-label>
                  <nz-form-control nzErrorTip="Enter a last name.">
                    <input nz-input id="admin-last" formControlName="lastName" autocomplete="off" />
                  </nz-form-control>
                </nz-form-item>
              </div>
              <nz-form-item>
                <nz-form-label nzRequired nzFor="admin-email">Email</nz-form-label>
                <nz-form-control nzErrorTip="Enter a valid email address.">
                  <input nz-input id="admin-email" type="email" formControlName="email" autocomplete="off" />
                </nz-form-control>
              </nz-form-item>
            </fieldset>
          }

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
  styles: [`
    .footer { display: flex; justify-content: flex-end; gap: var(--da-space-2); }
    fieldset { border: 0; border-top: 1px solid var(--da-border); padding: var(--da-space-3) 0 0; margin: var(--da-space-2) 0 0; }
    legend { font-weight: 600; color: var(--da-ink); padding-right: var(--da-space-2); }
    .hint { color: var(--da-ink-2); font-size: 13px; margin: 0 0 var(--da-space-2); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    @media (max-width: 480px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClinicFormDrawerComponent {
  readonly open = input(false);
  readonly clinic = input<Clinic | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Clinic>();
  /** After creating a clinic: the first admin's invite (its link is shown once). */
  readonly invited = output<StaffInvite>();

  private readonly clinicsService = inject(ClinicsService);
  private readonly staffService = inject(StaffService);
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
  readonly countries = toSignal(this.lookups.countries().pipe(catchError(() => of([] as CountryOption[]))), {
    initialValue: [] as CountryOption[]
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
    country: ['US', Validators.required],
    slotIntervalMinutes: [15, Validators.required]
  });

  /** New clinics only: who becomes the clinic's first admin. */
  readonly adminForm = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]]
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
        phone: formatPhone(clinic?.phone),
        address: clinic?.address ?? '',
        timeZone: clinic?.timeZone || browserZone() || DEFAULT_ZONE,
        country: clinic?.country || 'US',
        slotIntervalMinutes: clinic?.slotIntervalMinutes ?? 15
      });
      this.adminForm.reset({ firstName: '', lastName: '', email: '' });
    });
  }

  save(): void {
    if (this.saving()) return;
    const clinic = this.clinic();
    const needsAdmin = !clinic;
    if (this.form.invalid || (needsAdmin && this.adminForm.invalid)) {
      touchAll(this.form);
      if (needsAdmin) touchAll(this.adminForm);
      return;
    }

    const request: ClinicRequest = this.form.getRawValue();
    const call = clinic ? this.clinicsService.update(clinic.id, request) : this.clinicsService.create(request);

    this.saving.set(true);
    this.serverError.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result) => {
        if (clinic) {
          this.messages.success($localize`:@@clinicForm.updated:Clinic updated.`);
          this.saved.emit(result);
          return;
        }
        this.inviteFirstAdmin(result);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse) {
          const errors: string[] | undefined = error.error?.errors;
          this.serverError.set(errors?.length ? errors.join(' ') : describeHttpError(error));
        }
      }
    });
  }

  /**
   * Second step of creating a clinic. The clinic already exists at this point, so if the invite fails (e.g. the
   * email already has an account) we still close the form and say how to invite from the clinic's Staff instead.
   */
  private inviteFirstAdmin(created: Clinic): void {
    const admin = this.adminForm.getRawValue();
    this.saving.set(true);
    this.staffService
      .invite({ firstName: admin.firstName.trim(), lastName: admin.lastName.trim(), email: admin.email.trim(), role: 'ClinicAdmin' },
        created.id)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (invite) => {
          this.messages.success($localize`:@@clinicForm.created:Clinic created.`);
          this.saved.emit(created);
          this.invited.emit(invite);
        },
        error: (error: unknown) => {
          this.messages.warning(`${created.name} was created, but the admin invite failed: ${saveErrorText(error)} ` +
            'Open the clinic’s Staff to invite an admin.', { nzDuration: 10000 });
          this.saved.emit(created);
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
