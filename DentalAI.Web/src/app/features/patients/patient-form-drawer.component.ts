import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { format, parseISO } from 'date-fns';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { finalize } from 'rxjs';
import { Patient, PatientRequest } from '../../core/models/patient.models';
import { ClinicOptionsService } from '../../core/services/clinic-options.service';
import { PatientsService } from '../../core/services/patients.service';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { optional, saveErrorText, touchAll } from '../../shared/forms';

export const GENDERS = ['Female', 'Male', 'Non-binary', 'Prefer not to say'];
const PATIENT_PHONE_PATTERN = /^[0-9+\-\s().]{7,30}$/;

/** Add or edit a patient. Pass `patient` to edit, or null to create. */
@Component({
  selector: 'app-patient-form-drawer',
  standalone: true,
  imports: [ReactiveFormsModule, NzDatePickerModule, NzFormModule, NzInputModule, NzSelectModule, FormDrawerComponent],
  template: `
    <app-form-drawer [open]="open()" [title]="patient() ? 'Edit patient' : 'New patient'"
                     [submitLabel]="patient() ? 'Save changes' : 'Add patient'" [saving]="saving()" [error]="error()"
                     (closed)="closed.emit()" (submitted)="save()">
      <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
        @if (clinicOptions.isSuperAdmin()) {
          <nz-form-item>
            <nz-form-label nzRequired nzFor="patient-clinic">Clinic</nz-form-label>
            <nz-form-control nzErrorTip="Choose the patient's clinic.">
              <nz-select nzId="patient-clinic" formControlName="clinicId" nzShowSearch nzPlaceHolder="Select a clinic"
                         [nzLoading]="!clinicOptions.clinics().length">
                @for (clinic of clinicOptions.clinics(); track clinic.id) {
                  <nz-option [nzValue]="clinic.id" [nzLabel]="clinic.name" />
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>
        }

        <div class="row">
          <nz-form-item>
            <nz-form-label nzRequired nzFor="patient-first">First name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a first name.">
              <input nz-input id="patient-first" formControlName="firstName" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired nzFor="patient-last">Last name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a last name.">
              <input nz-input id="patient-last" formControlName="lastName" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
        </div>

        <nz-form-item>
          <nz-form-label nzRequired nzFor="patient-phone">Phone</nz-form-label>
          <nz-form-control nzErrorTip="Enter a phone number, e.g. +1 212 555 0100.">
            <input nz-input id="patient-phone" type="tel" formControlName="phone" autocomplete="off" />
          </nz-form-control>
        </nz-form-item>

        <nz-form-item>
          <nz-form-label nzFor="patient-email">Email</nz-form-label>
          <nz-form-control nzErrorTip="Enter a valid email address, or leave it empty.">
            <input nz-input id="patient-email" type="email" formControlName="email" autocomplete="off" />
          </nz-form-control>
        </nz-form-item>

        <div class="row">
          <nz-form-item>
            <nz-form-label nzFor="patient-dob">Date of birth</nz-form-label>
            <nz-form-control>
              <nz-date-picker nzId="patient-dob" formControlName="dateOfBirth" nzFormat="MM/dd/yyyy"
                              nzPlaceHolder="MM/DD/YYYY" [nzDisabledDate]="isFuture"
                              [nzDefaultPickerValue]="typicalBirthYear" class="full" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzFor="patient-gender">Gender</nz-form-label>
            <nz-form-control>
              <nz-select nzId="patient-gender" formControlName="gender" nzAllowClear nzPlaceHolder="Not specified">
                @for (gender of genders; track gender) {
                  <nz-option [nzValue]="gender" [nzLabel]="gender" />
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>
        </div>

        <nz-form-item>
          <nz-form-label nzFor="patient-notes">Notes</nz-form-label>
          <nz-form-control nzErrorTip="Notes can be up to 2,000 characters.">
            <textarea nz-input id="patient-notes" rows="3" formControlName="notes"
                      placeholder="Allergies, preferences, anything the team should know"></textarea>
          </nz-form-control>
        </nz-form-item>
        <button type="submit" hidden></button>
      </form>
    </app-form-drawer>
  `,
  styles: [`.row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
            .full { width: 100%; }
            @media (max-width: 480px) { .row { grid-template-columns: 1fr; } }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientFormDrawerComponent {
  readonly open = input(false);
  readonly patient = input<Patient | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Patient>();

  readonly clinicOptions = inject(ClinicOptionsService);
  private readonly patientsService = inject(PatientsService);
  private readonly messages = inject(NzMessageService);

  readonly genders = GENDERS;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly isFuture = (date: Date) => date > new Date();
  /** The calendar opens about 30 years back, not on today. Date-only values are local dates (no time zone). */
  readonly typicalBirthYear = new Date(new Date().getFullYear() - 30, 0, 1);

  readonly form = inject(NonNullableFormBuilder).group({
    clinicId: [''],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phone: ['', [Validators.required, Validators.maxLength(30), Validators.pattern(PATIENT_PHONE_PATTERN)]],
    email: ['', [Validators.email, Validators.maxLength(200)]],
    dateOfBirth: [null as Date | null],
    gender: [null as string | null],
    notes: ['', Validators.maxLength(2000)]
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const patient = this.patient();
      const superAdmin = this.clinicOptions.isSuperAdmin();
      untracked(() => {
        this.error.set(null);
        this.form.controls.clinicId.setValidators(superAdmin ? Validators.required : null);
        this.form.reset({
          clinicId: patient?.clinicId ?? '',
          firstName: patient?.firstName ?? '',
          lastName: patient?.lastName ?? '',
          phone: patient?.phone ?? '',
          email: patient?.email ?? '',
          dateOfBirth: patient?.dateOfBirth ? parseISO(patient.dateOfBirth) : null,
          gender: patient?.gender ?? null,
          notes: patient?.notes ?? ''
        });
      });
    });
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }

    const value = this.form.getRawValue();
    const request: PatientRequest = {
      clinicId: value.clinicId || null,
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim(),
      phone: value.phone.trim(),
      email: optional(value.email),
      dateOfBirth: value.dateOfBirth ? format(value.dateOfBirth, 'yyyy-MM-dd') : null,
      gender: value.gender || null,
      notes: optional(value.notes)
    };
    const patient = this.patient();
    const call = patient ? this.patientsService.update(patient.id, request) : this.patientsService.create(request);

    this.saving.set(true);
    this.error.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result) => {
        this.messages.success(patient ? 'Patient updated.' : 'Patient added.');
        this.saved.emit(result);
      },
      error: (error: unknown) => this.error.set(saveErrorText(error))
    });
  }
}
