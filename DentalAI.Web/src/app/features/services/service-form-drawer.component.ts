import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { finalize } from 'rxjs';
import { DentalService, ServiceRequest } from '../../core/models/service.models';
import { ClinicOptionsService } from '../../core/services/clinic-options.service';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { optional, saveErrorText, touchAll } from '../../shared/forms';

/** Add or edit a bookable service. Pass `service` to edit, or null to create. */
@Component({
  selector: 'app-service-form-drawer',
  standalone: true,
  imports: [
    ReactiveFormsModule, NzFormModule, NzInputModule, NzInputNumberModule, NzSelectModule, NzSwitchModule,
    FormDrawerComponent
  ],
  template: `
    <app-form-drawer [open]="open()" [title]="service() ? 'Edit service' : 'New service'"
                     [submitLabel]="service() ? 'Save changes' : 'Add service'" [saving]="saving()" [error]="error()"
                     (closed)="closed.emit()" (submitted)="save()">
      <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
        @if (clinicOptions.isSuperAdmin() && !service()) {
          <nz-form-item>
            <nz-form-label nzRequired nzFor="service-clinic">Clinic</nz-form-label>
            <nz-form-control nzErrorTip="Choose the clinic that offers this service.">
              <nz-select nzId="service-clinic" formControlName="clinicId" nzShowSearch nzPlaceHolder="Select a clinic"
                         [nzLoading]="!clinicOptions.clinics().length">
                @for (clinic of clinicOptions.clinics(); track clinic.id) {
                  <nz-option [nzValue]="clinic.id" [nzLabel]="clinic.name" />
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>
        }

        <nz-form-item>
          <nz-form-label nzRequired nzFor="service-name">Name</nz-form-label>
          <nz-form-control nzErrorTip="Enter a name, e.g. Cleaning.">
            <input nz-input id="service-name" formControlName="name" autocomplete="off" />
          </nz-form-control>
        </nz-form-item>

        <div class="row">
          <nz-form-item>
            <nz-form-label nzRequired nzFor="service-duration">Duration (minutes)</nz-form-label>
            <nz-form-control nzErrorTip="Between 5 and 480 minutes."
                             nzExtra="Sets the length of each appointment.">
              <nz-input-number nzId="service-duration" formControlName="durationMinutes" [nzMin]="5" [nzMax]="480"
                               [nzStep]="5" [nzPrecision]="0" class="full" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired nzFor="service-price">Price (USD)</nz-form-label>
            <nz-form-control nzErrorTip="Enter 0 or more.">
              <nz-input-number nzId="service-price" formControlName="price" [nzMin]="0" [nzStep]="5" [nzPrecision]="2"
                               nzPrefix="$" class="full" />
            </nz-form-control>
          </nz-form-item>
        </div>

        <nz-form-item>
          <nz-form-label nzFor="service-description">Description</nz-form-label>
          <nz-form-control nzErrorTip="Up to 1,000 characters.">
            <textarea nz-input id="service-description" rows="3" formControlName="description"></textarea>
          </nz-form-control>
        </nz-form-item>

        <nz-form-item>
          <nz-form-control nzExtra="Inactive services stay on past appointments but can't be booked.">
            <label class="switch">
              <nz-switch formControlName="isActive" /> <span>Can be booked</span>
            </label>
          </nz-form-control>
        </nz-form-item>
        <button type="submit" hidden></button>
      </form>
    </app-form-drawer>
  `,
  styles: [`.row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
            .full { width: 100%; }
            .switch { display: inline-flex; align-items: center; gap: var(--da-space-2); cursor: pointer; }
            @media (max-width: 480px) { .row { grid-template-columns: 1fr; } }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ServiceFormDrawerComponent {
  readonly open = input(false);
  readonly service = input<DentalService | null>(null);
  readonly closed = output<void>();
  readonly saved = output<DentalService>();

  readonly clinicOptions = inject(ClinicOptionsService);
  private readonly servicesCatalog = inject(ServicesCatalogService);
  private readonly messages = inject(NzMessageService);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    clinicId: [''],
    name: ['', [Validators.required, Validators.maxLength(150)]],
    durationMinutes: [30, [Validators.required, Validators.min(5), Validators.max(480)]],
    price: [0, [Validators.required, Validators.min(0)]],
    description: ['', Validators.maxLength(1000)],
    isActive: [true]
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const service = this.service();
      const needsClinic = this.clinicOptions.isSuperAdmin() && !service;
      untracked(() => {
        this.error.set(null);
        this.form.controls.clinicId.setValidators(needsClinic ? Validators.required : null);
        this.form.reset({
          clinicId: '',
          name: service?.name ?? '',
          durationMinutes: service?.durationMinutes ?? 30,
          price: service?.price ?? 0,
          description: service?.description ?? '',
          isActive: service?.isActive ?? true
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
    const request: ServiceRequest = {
      clinicId: value.clinicId || null,
      name: value.name.trim(),
      description: optional(value.description),
      price: value.price,
      durationMinutes: value.durationMinutes,
      isActive: value.isActive
    };
    const service = this.service();
    const call = service ? this.servicesCatalog.update(service.id, request) : this.servicesCatalog.create(request);

    this.saving.set(true);
    this.error.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (result) => {
        this.messages.success(service ? 'Service updated.' : 'Service added.');
        this.saved.emit(result);
      },
      error: (error: unknown) => this.error.set(saveErrorText(error))
    });
  }
}
