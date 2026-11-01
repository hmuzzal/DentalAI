import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAutocompleteModule } from 'ng-zorro-antd/auto-complete';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { finalize } from 'rxjs';
import { Doctor, DoctorRequest } from '../../core/models/doctor.models';
import { ClinicOptionsService } from '../../core/services/clinic-options.service';
import { DoctorsService } from '../../core/services/doctors.service';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { PHONE_PATTERN, saveErrorText, touchAll } from '../../shared/forms';

const SPECIALTIES = [
  'General Dentist', 'Dental Hygienist', 'Orthodontist', 'Endodontist', 'Periodontist',
  'Oral Surgeon', 'Pediatric Dentist', 'Prosthodontist'
];

/** Add or edit a doctor. Pass `doctor` to edit, or null to create. */
@Component({
  selector: 'app-doctor-form-drawer',
  standalone: true,
  imports: [ReactiveFormsModule, NzAutocompleteModule, NzFormModule, NzInputModule, NzSelectModule, FormDrawerComponent],
  template: `
    <app-form-drawer [open]="open()" [title]="doctor() ? 'Edit doctor' : 'New doctor'"
                     [submitLabel]="doctor() ? 'Save changes' : 'Add doctor'" [saving]="saving()" [error]="error()"
                     (closed)="closed.emit()" (submitted)="save()">
      <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
        @if (clinicOptions.isSuperAdmin()) {
          <nz-form-item>
            <nz-form-label nzRequired nzFor="doctor-clinic">Clinic</nz-form-label>
            <nz-form-control nzErrorTip="Choose the doctor's clinic.">
              <nz-select nzId="doctor-clinic" formControlName="clinicId" nzShowSearch nzPlaceHolder="Select a clinic"
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
            <nz-form-label nzRequired nzFor="doctor-first">First name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a first name.">
              <input nz-input id="doctor-first" formControlName="firstName" autocomplete="given-name" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired nzFor="doctor-last">Last name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a last name.">
              <input nz-input id="doctor-last" formControlName="lastName" autocomplete="family-name" />
            </nz-form-control>
          </nz-form-item>
        </div>

        <nz-form-item>
          <nz-form-label nzRequired nzFor="doctor-specialty">Specialty</nz-form-label>
          <nz-form-control nzErrorTip="Enter a specialty, e.g. General Dentist.">
            <input nz-input id="doctor-specialty" formControlName="specialty" [nzAutocomplete]="specialties" />
            <nz-autocomplete #specialties>
              @for (option of specialtyOptions(); track option) {
                <nz-auto-option [nzValue]="option">{{ option }}</nz-auto-option>
              }
            </nz-autocomplete>
          </nz-form-control>
        </nz-form-item>

        <nz-form-item>
          <nz-form-label nzRequired nzFor="doctor-email">Email</nz-form-label>
          <nz-form-control nzErrorTip="Enter a valid email address.">
            <input nz-input id="doctor-email" type="email" formControlName="email" autocomplete="email" />
          </nz-form-control>
        </nz-form-item>

        <nz-form-item>
          <nz-form-label nzRequired nzFor="doctor-phone">Phone</nz-form-label>
          <nz-form-control nzErrorTip="Enter a phone number, e.g. +1 212 555 0100.">
            <input nz-input id="doctor-phone" type="tel" formControlName="phone" autocomplete="tel" />
          </nz-form-control>
        </nz-form-item>
        <button type="submit" hidden></button>
      </form>
    </app-form-drawer>
  `,
  styles: [`.row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
            @media (max-width: 480px) { .row { grid-template-columns: 1fr; } }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DoctorFormDrawerComponent {
  readonly open = input(false);
  readonly doctor = input<Doctor | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Doctor>();

  readonly clinicOptions = inject(ClinicOptionsService);
  private readonly doctorsService = inject(DoctorsService);
  private readonly messages = inject(NzMessageService);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    clinicId: [''],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    specialty: ['', [Validators.required, Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
    phone: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]]
  });

  private readonly specialtyText = signal('');
  readonly specialtyOptions = computed(() => {
    const text = this.specialtyText().toLowerCase();
    return SPECIALTIES.filter((s) => s.toLowerCase().includes(text));
  });

  constructor() {
    this.form.controls.specialty.valueChanges.subscribe((value) => this.specialtyText.set(value ?? ''));

    effect(() => {
      if (!this.open()) return;
      const doctor = this.doctor();
      const superAdmin = this.clinicOptions.isSuperAdmin();
      untracked(() => {
        this.error.set(null);
        const clinicId = this.form.controls.clinicId;
        clinicId.setValidators(superAdmin ? Validators.required : null);
        this.form.reset({
          clinicId: doctor?.clinicId ?? '',
          firstName: doctor?.firstName ?? '',
          lastName: doctor?.lastName ?? '',
          specialty: doctor?.specialty ?? '',
          email: doctor?.email ?? '',
          phone: doctor?.phone ?? ''
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
    const request: DoctorRequest = {
      clinicId: value.clinicId || null,
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim(),
      specialty: value.specialty.trim(),
      email: value.email.trim(),
      phone: value.phone.trim()
    };
    const doctor = this.doctor();
    const call = doctor ? this.doctorsService.update(doctor.id, request) : this.doctorsService.create(request);

    this.saving.set(true);
    this.error.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result) => {
        this.messages.success(doctor ? 'Doctor updated.' : 'Doctor added.');
        this.saved.emit(result);
      },
      error: (error: unknown) => this.error.set(saveErrorText(error))
    });
  }
}
