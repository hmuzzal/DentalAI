import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { finalize } from 'rxjs';
import { ContactMethod } from '../../core/models/patient.models';
import { PortalProfile } from '../../core/models/portal.models';
import { PortalApiService } from '../../core/services/portal.service';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { optional, PHONE_PATTERN, saveErrorText, touchAll } from '../../shared/forms';
import { PageStateComponent } from '../../shared/page-state.component';
import { formatPhone } from '../../shared/phone.pipe';
import { LoadStatus } from '../../shared/remote-data';
import { US_STATES } from '../../shared/us-states';

/** /p/:slug/profile — the patient's contact details, address and text-message consent. */
@Component({
  selector: 'app-portal-profile',
  standalone: true,
  imports: [
    ReactiveFormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzCheckboxModule, NzFormModule, NzInputModule, NzRadioModule,
    NzSelectModule, DateOnlyPipe, PageStateComponent
  ],
  template: `
    <h1>My details</h1>
    <app-page-state [status]="status()" (retry)="load()" errorText="We could not load your details.">
      @if (profile(); as p) {
        <nz-card class="who">
          <strong>{{ p.firstName }} {{ p.lastName }}</strong>
          @if (p.dateOfBirth) { <span class="muted">Born {{ p.dateOfBirth | dateOnly }}</span> }
          <small class="muted">To change your name or date of birth, please contact the clinic.</small>
        </nz-card>
      }
      <nz-card>
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
          <div class="row">
            <nz-form-item>
              <nz-form-label nzRequired nzFor="phone">Mobile number</nz-form-label>
              <nz-form-control nzErrorTip="Enter a valid phone number.">
                <input nz-input id="phone" type="tel" formControlName="phone" autocomplete="tel" />
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzFor="email">Email</nz-form-label>
              <nz-form-control nzErrorTip="Enter a valid email address.">
                <input nz-input id="email" type="email" formControlName="email" autocomplete="email" />
              </nz-form-control>
            </nz-form-item>
          </div>
          <nz-form-item>
            <nz-form-label nzFor="line1">Address</nz-form-label>
            <nz-form-control>
              <input nz-input id="line1" formControlName="addressLine1" autocomplete="address-line1" placeholder="Street address" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-control>
              <input nz-input formControlName="addressLine2" autocomplete="address-line2" placeholder="Apt, suite (optional)"
                     aria-label="Apartment or suite" />
            </nz-form-control>
          </nz-form-item>
          <div class="row row--3">
            <nz-form-item>
              <nz-form-label nzFor="city">City</nz-form-label>
              <nz-form-control><input nz-input id="city" formControlName="city" autocomplete="address-level2" /></nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzFor="state">State</nz-form-label>
              <nz-form-control>
                <nz-select nzId="state" formControlName="state" nzShowSearch nzAllowClear nzPlaceHolder="State">
                  @for (s of states; track s.code) { <nz-option [nzValue]="s.code" [nzLabel]="s.name" /> }
                </nz-select>
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzFor="zip">ZIP</nz-form-label>
              <nz-form-control nzErrorTip="Enter a valid ZIP code.">
                <input nz-input id="zip" formControlName="postalCode" autocomplete="postal-code" />
              </nz-form-control>
            </nz-form-item>
          </div>
          <nz-form-item>
            <nz-form-label>How should the clinic contact you?</nz-form-label>
            <nz-form-control>
              <nz-radio-group formControlName="preferredContactMethod">
                <label nz-radio nzValue="Phone">Phone call</label>
                <label nz-radio nzValue="Sms">Text message</label>
                <label nz-radio nzValue="Email">Email</label>
              </nz-radio-group>
            </nz-form-control>
          </nz-form-item>
          <label nz-checkbox formControlName="smsOptIn" class="consent">
            Text me appointment confirmations and reminders. Message and data rates may apply. Reply STOP to opt out at any time.
          </label>
          @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
          <div class="actions">
            <button nz-button nzType="primary" [nzLoading]="saving()" [disabled]="form.pristine">Save</button>
          </div>
        </form>
      </nz-card>
    </app-page-state>
  `,
  styles: [`
    h1 { font-size: 24px; margin: 0 0 var(--da-space-4); }
    .muted { color: var(--da-ink-3); }
    .who { margin-bottom: var(--da-space-3); }
    .who ::ng-deep .ant-card-body { display: grid; gap: 2px; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .row--3 { grid-template-columns: 2fr 1fr 1fr; }
    .consent { color: var(--da-ink-2); font-size: 13px; }
    .gap { margin-top: var(--da-space-3); }
    .actions { display: flex; justify-content: flex-end; margin-top: var(--da-space-3); }
    @media (max-width: 560px) { .row, .row--3 { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalProfileComponent {
  private readonly api = inject(PortalApiService);
  private readonly messages = inject(NzMessageService);

  readonly states = US_STATES;
  readonly status = signal<LoadStatus>('loading');
  readonly profile = signal<PortalProfile | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    phone: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]],
    email: ['', [Validators.email, Validators.maxLength(200)]],
    addressLine1: ['', Validators.maxLength(200)],
    addressLine2: ['', Validators.maxLength(200)],
    city: ['', Validators.maxLength(100)],
    state: [null as string | null],
    postalCode: ['', Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9\- ]{1,18}[A-Za-z0-9]$/)],
    preferredContactMethod: ['Phone' as ContactMethod],
    smsOptIn: [false]
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.status.set('loading');
    this.api.profile().subscribe({
      next: (profile) => this.fill(profile),
      error: () => this.status.set('error')
    });
  }

  save(): void {
    if (this.saving()) return;
    const v = this.form.getRawValue();
    if (v.preferredContactMethod === 'Sms' && !v.smsOptIn) {
      this.error.set('To be contacted by text, tick the box to agree to text messages.');
      return;
    }
    if (v.preferredContactMethod === 'Email' && !v.email.trim()) {
      this.error.set('Add an email address to be contacted by email.');
      return;
    }
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api.updateProfile({
      phone: v.phone.trim(),
      email: optional(v.email),
      addressLine1: optional(v.addressLine1),
      addressLine2: optional(v.addressLine2),
      city: optional(v.city),
      state: v.state,
      postalCode: optional(v.postalCode),
      preferredContactMethod: v.preferredContactMethod,
      smsOptIn: v.smsOptIn
    }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (profile) => {
        this.fill(profile);
        this.messages.success('Your details are saved.');
      },
      error: (error: unknown) => this.error.set(saveErrorText(error))
    });
  }

  private fill(profile: PortalProfile): void {
    this.profile.set(profile);
    this.form.reset({
      phone: formatPhone(profile.phone),
      email: profile.email ?? '',
      addressLine1: profile.addressLine1 ?? '',
      addressLine2: profile.addressLine2 ?? '',
      city: profile.city ?? '',
      state: profile.state,
      postalCode: profile.postalCode ?? '',
      preferredContactMethod: profile.preferredContactMethod,
      smsOptIn: profile.smsOptIn
    });
    this.status.set('ready');
  }
}
