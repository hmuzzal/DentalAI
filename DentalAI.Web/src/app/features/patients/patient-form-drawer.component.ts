import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { format, parseISO } from 'date-fns';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzAutocompleteModule } from 'ng-zorro-antd/auto-complete';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTabsModule } from 'ng-zorro-antd/tabs';
import { finalize, merge } from 'rxjs';
import {
  CONTACT_METHOD_LABELS, ContactMethod, InsurancePriority, InsuranceRelationship, Patient, PatientInsurance,
  PatientRequest, RELATIONSHIP_LABELS
} from '../../core/models/patient.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { PatientsService } from '../../core/services/patients.service';
import { InvoicesService } from '../../core/services/invoices.service';
import { formatMoney } from '../../shared/money.pipe';
import { RouterLink } from '@angular/router';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { optional, saveErrorText, touchAll } from '../../shared/forms';
import { formatPhone, phoneCountryHint } from '../../shared/phone.pipe';
import { US_STATES } from '../../shared/us-states';

export const GENDERS = ['Female', 'Male', 'Non-binary', 'Prefer not to say'];
const PATIENT_PHONE_PATTERN = /^[0-9+\-\s().]{7,30}$/;
const TABS = ['Details', 'Contact', 'Insurance'] as const;
type Tab = (typeof TABS)[number];

/** Add or edit a patient: details, contact (address, preferred method, SMS consent) and insurance. */
@Component({
  selector: 'app-patient-form-drawer',
  standalone: true,
  imports: [
    NgTemplateOutlet, RouterLink, ReactiveFormsModule, NzAlertModule, NzAutocompleteModule, NzButtonModule, NzCheckboxModule,
    NzDatePickerModule, NzFormModule, NzInputModule, NzRadioModule, NzSelectModule, NzSkeletonModule, NzSwitchModule,
    NzTabsModule, FormDrawerComponent
  ],
  template: `
    <app-form-drawer [open]="open()" [title]="patient() ? 'Edit patient' : 'New patient'" [width]="560"
                     [submitLabel]="patient() ? 'Save changes' : 'Add patient'" [saving]="saving()" [error]="error()"
                     (closed)="closed.emit()" (submitted)="save()">
      @if (loading()) {
        <nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 10 }" />
      } @else if (loadError()) {
        <nz-alert nzType="error" nzShowIcon nzMessage="We could not load this patient." [nzAction]="retry" />
        <ng-template #retry><button nz-button nzSize="small" (click)="load()">Try again</button></ng-template>
      } @else {
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
          <nz-tabs [nzSelectedIndex]="tabIndex()" (nzSelectedIndexChange)="tabIndex.set($event)" nzSize="small">
            @for (tab of tabs; track tab) {
              <nz-tab [nzTitle]="tabTitle" [nzForceRender]="true">
                <ng-template #tabTitle>
                  {{ tab }} @if (tabHasErrors()[tab]) { <span class="dot" aria-label="has problems"></span> }
                </ng-template>
                @switch (tab) {
                  @case ('Details') { <ng-container [ngTemplateOutlet]="details" /> }
                  @case ('Contact') { <ng-container [ngTemplateOutlet]="contact" /> }
                  @case ('Insurance') { <ng-container [ngTemplateOutlet]="insurance" /> }
                }
              </nz-tab>
            }
          </nz-tabs>
          <button type="submit" hidden></button>
        </form>
      }
    </app-form-drawer>

    <!-- Details -->
    <ng-template #details>
      <ng-container [formGroup]="form">
        @if (balance(); as owed) {
          <nz-alert class="balance" nzType="warning" nzShowIcon [nzMessage]="'Balance due: ' + owed" [nzAction]="viewInvoices" />
          <ng-template #viewInvoices>
            <a nz-button nzSize="small" routerLink="/invoices" [queryParams]="{ patient: patient()?.id, filter: 'Unpaid' }" (click)="closed.emit()">View invoices</a>
          </ng-template>
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
                @for (gender of genders; track gender) { <nz-option [nzValue]="gender" [nzLabel]="gender" /> }
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
      </ng-container>
    </ng-template>

    <!-- Contact -->
    <ng-template #contact>
      <ng-container [formGroup]="form">
        <div class="row">
          <nz-form-item>
            <nz-form-label nzRequired nzFor="patient-phone">Phone</nz-form-label>
            <nz-form-control nzErrorTip="Enter a phone number, e.g. (212) 555-0142." [nzExtra]="phoneHint()">
              <input nz-input id="patient-phone" type="tel" formControlName="phone" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzFor="patient-email">Email</nz-form-label>
            <nz-form-control nzErrorTip="Enter a valid email address, or leave it empty.">
              <input nz-input id="patient-email" type="email" formControlName="email" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
        </div>

        <fieldset>
          <legend>Address</legend>
          <nz-form-item>
            <nz-form-label nzFor="patient-address1">Street address</nz-form-label>
            <nz-form-control>
              <input nz-input id="patient-address1" formControlName="addressLine1" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzFor="patient-address2">Apt, suite, unit</nz-form-label>
            <nz-form-control>
              <input nz-input id="patient-address2" formControlName="addressLine2" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          <div class="row row--address">
            <nz-form-item>
              <nz-form-label nzFor="patient-city">City</nz-form-label>
              <nz-form-control>
                <input nz-input id="patient-city" formControlName="city" autocomplete="off" />
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzFor="patient-state">State</nz-form-label>
              <nz-form-control>
                <input nz-input id="patient-state" formControlName="state" [nzAutocomplete]="states" autocomplete="off" />
                <nz-autocomplete #states>
                  @for (state of stateOptions(); track state.code) {
                    <nz-auto-option [nzValue]="state.code" [nzLabel]="state.code">{{ state.code }} · {{ state.name }}</nz-auto-option>
                  }
                </nz-autocomplete>
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzFor="patient-zip">ZIP code</nz-form-label>
              <nz-form-control nzErrorTip="Enter a valid ZIP code, e.g. 10118.">
                <input nz-input id="patient-zip" formControlName="postalCode" autocomplete="off" />
              </nz-form-control>
            </nz-form-item>
          </div>
        </fieldset>

        <nz-form-item>
          <nz-form-label>Preferred contact</nz-form-label>
          <nz-form-control [nzValidateStatus]="contactProblem() ? 'error' : ''" [nzErrorTip]="contactProblem() ?? ''">
            <nz-radio-group formControlName="preferredContactMethod">
              @for (method of contactMethods; track method) {
                <label nz-radio [nzValue]="method">{{ contactLabels[method] }}</label>
              }
            </nz-radio-group>
          </nz-form-control>
        </nz-form-item>

        <nz-form-item>
          <nz-form-control>
            <label nz-checkbox formControlName="smsOptIn">
              Patient agrees to receive appointment text messages. Message and data rates may apply; reply STOP to opt out.
            </label>
            @if (consentNote(); as note) { <div class="muted note">{{ note }}</div> }
          </nz-form-control>
        </nz-form-item>
      </ng-container>
    </ng-template>

    <!-- Insurance -->
    <ng-template #insurance>
      <ng-container [formGroup]="form">
        @for (slot of insuranceSlots; track slot.priority) {
          <section class="policy" [class.policy--off]="!form.controls[slot.toggle].value">
            <header>
              <label class="switch">
                <nz-switch [formControlName]="slot.toggle" nzSize="small"
                           [nzDisabled]="slot.priority === 'Secondary' && !form.controls.hasPrimary.value" />
                <strong>{{ slot.priority }} insurance</strong>
              </label>
              @if (slot.priority === 'Secondary' && !form.controls.hasPrimary.value) {
                <span class="muted">Add a primary plan first.</span>
              }
            </header>

            @if (form.controls[slot.toggle].value) {
              <div [formGroupName]="slot.group">
                <div class="row">
                  <nz-form-item>
                    <nz-form-label nzRequired [nzFor]="slot.priority + '-carrier'">Carrier</nz-form-label>
                    <nz-form-control nzErrorTip="Enter the insurance company.">
                      <input nz-input [id]="slot.priority + '-carrier'" formControlName="carrierName"
                             placeholder="e.g. Delta Dental" autocomplete="off" />
                    </nz-form-control>
                  </nz-form-item>
                  <nz-form-item>
                    <nz-form-label nzRequired [nzFor]="slot.priority + '-member'">Member ID</nz-form-label>
                    <nz-form-control nzErrorTip="Enter the member ID from the card.">
                      <input nz-input [id]="slot.priority + '-member'" formControlName="memberId" autocomplete="off" />
                    </nz-form-control>
                  </nz-form-item>
                </div>
                <div class="row">
                  <nz-form-item>
                    <nz-form-label [nzFor]="slot.priority + '-group'">Group number</nz-form-label>
                    <nz-form-control>
                      <input nz-input [id]="slot.priority + '-group'" formControlName="groupNumber" autocomplete="off" />
                    </nz-form-control>
                  </nz-form-item>
                  <nz-form-item>
                    <nz-form-label nzRequired [nzFor]="slot.priority + '-relationship'">Relationship to subscriber</nz-form-label>
                    <nz-form-control>
                      <nz-select [nzId]="slot.priority + '-relationship'" formControlName="relationshipToPatient">
                        @for (relationship of relationships; track relationship) {
                          <nz-option [nzValue]="relationship" [nzLabel]="relationshipLabels[relationship]" />
                        }
                      </nz-select>
                    </nz-form-control>
                  </nz-form-item>
                </div>
                @if (isSelf(slot.group)) {
                  <p class="muted note">The patient holds this plan, so they are the subscriber.</p>
                } @else {
                  <div class="row">
                    <nz-form-item>
                      <nz-form-label nzRequired [nzFor]="slot.priority + '-subscriber'">Subscriber name</nz-form-label>
                      <nz-form-control nzErrorTip="Enter the name of the person who holds the plan.">
                        <input nz-input [id]="slot.priority + '-subscriber'" formControlName="subscriberName" autocomplete="off" />
                      </nz-form-control>
                    </nz-form-item>
                    <nz-form-item>
                      <nz-form-label [nzFor]="slot.priority + '-subscriber-dob'">Subscriber date of birth</nz-form-label>
                      <nz-form-control>
                        <nz-date-picker [nzId]="slot.priority + '-subscriber-dob'" formControlName="subscriberDateOfBirth"
                                        nzFormat="MM/dd/yyyy" nzPlaceHolder="MM/DD/YYYY" [nzDisabledDate]="isFuture"
                                        [nzDefaultPickerValue]="typicalBirthYear" class="full" />
                      </nz-form-control>
                    </nz-form-item>
                  </div>
                }
              </div>
            }
          </section>
        }
      </ng-container>
    </ng-template>
  `,
  styles: [`
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .row--address { grid-template-columns: 2fr 1fr 1fr; }
    .balance { margin-bottom: var(--da-space-3); }
    .full { width: 100%; }
    .muted { color: var(--da-ink-2); }
    .note { font-size: 13px; margin: 4px 0 0; }
    .dot { display: inline-block; width: 6px; height: 6px; margin-left: 4px; border-radius: 50%; background: var(--da-danger); vertical-align: middle; }
    fieldset { border: 0; padding: 0; margin: 0 0 var(--da-space-2); }
    legend { font-size: 13px; font-weight: 600; color: var(--da-ink); margin-bottom: var(--da-space-2); border: 0; }
    .policy { border: 1px solid var(--da-border); border-radius: var(--da-radius); padding: var(--da-space-3); margin-bottom: var(--da-space-3); }
    .policy--off { background: var(--da-surface-2); }
    .policy header { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-2); }
    .policy header + div { margin-top: var(--da-space-3); }
    .switch { display: inline-flex; align-items: center; gap: var(--da-space-2); cursor: pointer; }
    @media (max-width: 480px) { .row, .row--address { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientFormDrawerComponent {
  readonly open = input(false);
  readonly patient = input<Patient | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Patient>();

  private readonly patientsService = inject(PatientsService);
  private readonly invoices = inject(InvoicesService);
  private readonly auth = inject(AuthStateService);
  private readonly messages = inject(NzMessageService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly tabs = TABS;
  readonly genders = GENDERS;
  readonly contactMethods: ContactMethod[] = ['Phone', 'Sms', 'Email'];
  readonly contactLabels = CONTACT_METHOD_LABELS;
  readonly relationships: InsuranceRelationship[] = ['Self', 'Spouse', 'Child', 'Other'];
  readonly relationshipLabels = RELATIONSHIP_LABELS;
  readonly insuranceSlots = [
    { priority: 'Primary' as InsurancePriority, toggle: 'hasPrimary' as const, group: 'primary' as const },
    { priority: 'Secondary' as InsurancePriority, toggle: 'hasSecondary' as const, group: 'secondary' as const }
  ];
  readonly isFuture = (date: Date) => date > new Date();
  /** The calendar opens about 30 years back, not on today. Date-only values are local dates (no time zone). */
  readonly typicalBirthYear = new Date(new Date().getFullYear() - 30, 0, 1);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal(false);
  /** What the patient still owes across their invoices, e.g. "$120.00" (null when nothing).  */
  readonly balance = signal<string | null>(null);
  readonly tabIndex = signal(0);
  /** Problems are only flagged on tabs after the first Save attempt. */
  private readonly submitted = signal(false);
  /** Bumped on every form change so the computed views below re-run. */
  private readonly formVersion = signal(0);
  /** The record being edited, as loaded (for the consent timestamps). */
  private readonly loaded = signal<Patient | null>(null);

  readonly form = this.fb.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    dateOfBirth: [null as Date | null],
    gender: [null as string | null],
    notes: ['', Validators.maxLength(2000)],

    phone: ['', [Validators.required, Validators.maxLength(30), Validators.pattern(PATIENT_PHONE_PATTERN)]],
    email: ['', [Validators.email, Validators.maxLength(200)]],
    addressLine1: ['', Validators.maxLength(200)],
    addressLine2: ['', Validators.maxLength(200)],
    city: ['', Validators.maxLength(100)],
    state: ['', Validators.maxLength(50)],
    postalCode: ['', [Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9\- ]{1,18}[A-Za-z0-9]$/)]],
    preferredContactMethod: ['Phone' as ContactMethod],
    smsOptIn: [false],

    hasPrimary: [false],
    primary: this.policyGroup(),
    hasSecondary: [false],
    secondary: this.policyGroup()
  }, { validators: contactRules });

  private readonly tabFields: Record<Tab, string[]> = {
    Details: ['firstName', 'lastName', 'dateOfBirth', 'gender', 'notes'],
    Contact: ['phone', 'email', 'addressLine1', 'addressLine2', 'city', 'state', 'postalCode', 'preferredContactMethod', 'smsOptIn'],
    Insurance: ['primary', 'secondary']
  };

  readonly tabHasErrors = computed<Record<Tab, boolean>>(() => {
    this.formVersion();
    const show = this.submitted();
    const contact = show && !!this.form.errors;
    return {
      Details: show && this.tabFields.Details.some((f) => this.form.get(f)?.invalid),
      Contact: contact || (show && this.tabFields.Contact.some((f) => this.form.get(f)?.invalid)),
      Insurance: show && this.tabFields.Insurance.some((f) => this.form.get(f)?.invalid)
    };
  });

  readonly contactProblem = computed(() => {
    this.formVersion();
    const errors = this.form.errors;
    if (errors?.['smsConsentRequired']) return 'Text messages need the patient’s SMS consent (tick the box below).';
    if (errors?.['emailRequired']) return 'Add an email address to use email.';
    return null;
  });

  readonly consentNote = computed(() => {
    this.formVersion();
    const loaded = this.loaded();
    const checked = this.form.controls.smsOptIn.value;
    if (!loaded || checked !== loaded.smsOptIn) {
      return checked ? 'Consent will be recorded with today’s date when you save.' : null;
    }
    if (checked && loaded.smsOptInAtUtc) return `Consent recorded ${format(parseISO(loaded.smsOptInAtUtc), 'MMM d, yyyy, h:mm a')}.`;
    if (!checked && loaded.smsOptOutAtUtc) return `Opted out ${format(parseISO(loaded.smsOptOutAtUtc), 'MMM d, yyyy, h:mm a')}.`;
    return null;
  });

  /** Which country numbers without +code are read as (the user's clinic's). */
  readonly phoneHint = computed(() => phoneCountryHint(this.auth.user()?.clinicCountry));

  private readonly stateText = signal('');
  readonly stateOptions = computed(() => {
    const text = this.stateText().trim().toLowerCase();
    return US_STATES.filter((s) => !text || s.code.toLowerCase().startsWith(text) || s.name.toLowerCase().includes(text));
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    merge(this.form.valueChanges, this.form.statusChanges)
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.formVersion.update((v) => v + 1));
    this.form.controls.state.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe((v) => this.stateText.set(v ?? ''));

    // Turning a plan off removes it from validation; turning the primary off also turns the secondary off.
    this.form.controls.hasPrimary.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe((on) => {
      this.togglePolicy('primary', on);
      if (!on) this.form.controls.hasSecondary.setValue(false);
    });
    this.form.controls.hasSecondary.valueChanges.pipe(takeUntilDestroyed(destroyRef))
      .subscribe((on) => this.togglePolicy('secondary', on));
    for (const group of ['primary', 'secondary'] as const) {
      this.form.controls[group].controls.relationshipToPatient.valueChanges.pipe(takeUntilDestroyed(destroyRef))
        .subscribe(() => this.applySubscriberRules(group));
    }

    effect(() => {
      if (!this.open()) return;
      const patient = this.patient();
      untracked(() => {
        this.error.set(null);
        this.submitted.set(false);
        this.tabIndex.set(0);
        if (patient) {
          this.load();
        } else {
          this.loaded.set(null);
          this.balance.set(null);
          this.fill(null);
        }
      });
    });
  }

  load(): void {
    const patient = this.patient();
    if (!patient) return;
    this.loading.set(true);
    this.loadError.set(false);
    // Lists don't include insurance, so fetch the full record.
    this.patientsService.getById(patient.id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (full) => {
        this.loadBalance(full.id);
        this.loaded.set(full);
        this.fill(full);
      },
      error: () => this.loadError.set(true)
    });
  }

  /** Sums what the patient owes on issued, unpaid invoices (per currency). */
  private loadBalance(patientId: string): void {
    this.balance.set(null);
    this.invoices.list({ patientId, outstandingOnly: true, page: 1, pageSize: 1 }).subscribe({
      next: (list) => {
        const owed = list.outstanding.filter((x) => x.amount > 0);
        this.balance.set(owed.length ? owed.map((x) => formatMoney(x.amount, x.currency)).join(' + ') : null);
      },
      error: () => this.balance.set(null)
    });
  }

  isSelf(group: 'primary' | 'secondary'): boolean {
    this.formVersion();
    return this.form.controls[group].controls.relationshipToPatient.value === 'Self';
  }

  save(): void {
    if (this.saving() || this.loading()) return;
    if (this.form.invalid) {
      this.submitted.set(true);
      touchAll(this.form);
      for (const group of ['primary', 'secondary'] as const) touchAll(this.form.controls[group]);
      const firstBad = TABS.findIndex((tab) => this.tabHasErrors()[tab]);
      if (firstBad >= 0) this.tabIndex.set(firstBad);
      return;
    }

    const v = this.form.getRawValue();
    const insurances: PatientInsurance[] = [];
    if (v.hasPrimary) insurances.push(this.toPolicy('Primary', v.primary));
    if (v.hasPrimary && v.hasSecondary) insurances.push(this.toPolicy('Secondary', v.secondary));

    const request: PatientRequest = {
      firstName: v.firstName.trim(),
      lastName: v.lastName.trim(),
      phone: v.phone.trim(),
      email: optional(v.email),
      dateOfBirth: v.dateOfBirth ? format(v.dateOfBirth, 'yyyy-MM-dd') : null,
      gender: v.gender || null,
      notes: optional(v.notes),
      addressLine1: optional(v.addressLine1),
      addressLine2: optional(v.addressLine2),
      city: optional(v.city),
      state: optional(v.state),
      postalCode: optional(v.postalCode),
      preferredContactMethod: v.preferredContactMethod,
      smsOptIn: v.smsOptIn,
      insurances
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

  private fill(patient: Patient | null): void {
    const primary = patient?.insurances.find((i) => i.priority === 'Primary') ?? null;
    const secondary = patient?.insurances.find((i) => i.priority === 'Secondary') ?? null;
    this.form.reset({
      firstName: patient?.firstName ?? '',
      lastName: patient?.lastName ?? '',
      dateOfBirth: patient?.dateOfBirth ? parseISO(patient.dateOfBirth) : null,
      gender: patient?.gender ?? null,
      notes: patient?.notes ?? '',
      phone: formatPhone(patient?.phone),
      email: patient?.email ?? '',
      addressLine1: patient?.addressLine1 ?? '',
      addressLine2: patient?.addressLine2 ?? '',
      city: patient?.city ?? '',
      state: patient?.state ?? '',
      postalCode: patient?.postalCode ?? '',
      preferredContactMethod: patient?.preferredContactMethod ?? 'Phone',
      smsOptIn: patient?.smsOptIn ?? false,
      hasPrimary: !!primary,
      primary: this.policyValue(primary),
      hasSecondary: !!secondary,
      secondary: this.policyValue(secondary)
    });
    this.togglePolicy('primary', !!primary);
    this.togglePolicy('secondary', !!secondary);
  }

  private policyGroup() {
    return this.fb.group({
      carrierName: ['', [Validators.required, Validators.maxLength(150)]],
      memberId: ['', [Validators.required, Validators.maxLength(50)]],
      groupNumber: ['', Validators.maxLength(50)],
      relationshipToPatient: ['Self' as InsuranceRelationship],
      subscriberName: ['', [Validators.required, Validators.maxLength(150)]],
      subscriberDateOfBirth: [null as Date | null]
    });
  }

  private policyValue(policy: PatientInsurance | null) {
    return {
      carrierName: policy?.carrierName ?? '',
      memberId: policy?.memberId ?? '',
      groupNumber: policy?.groupNumber ?? '',
      relationshipToPatient: policy?.relationshipToPatient ?? 'Self',
      subscriberName: policy && policy.relationshipToPatient !== 'Self' ? policy.subscriberName ?? '' : '',
      subscriberDateOfBirth: policy?.subscriberDateOfBirth && policy.relationshipToPatient !== 'Self'
        ? parseISO(policy.subscriberDateOfBirth) : null
    };
  }

  private toPolicy(priority: InsurancePriority, v: ReturnType<typeof this.policyValue>): PatientInsurance {
    const self = v.relationshipToPatient === 'Self';
    return {
      priority,
      carrierName: v.carrierName.trim(),
      memberId: v.memberId.trim(),
      groupNumber: optional(v.groupNumber),
      relationshipToPatient: v.relationshipToPatient,
      subscriberName: self ? null : v.subscriberName.trim(),
      subscriberDateOfBirth: !self && v.subscriberDateOfBirth ? format(v.subscriberDateOfBirth, 'yyyy-MM-dd') : null
    };
  }

  private togglePolicy(group: 'primary' | 'secondary', on: boolean): void {
    const control = this.form.controls[group];
    if (on) {
      control.enable({ emitEvent: false });
      this.applySubscriberRules(group);
    } else {
      control.disable({ emitEvent: false });
    }
    this.form.updateValueAndValidity();
  }

  /** Subscriber fields only apply when someone else holds the plan. */
  private applySubscriberRules(group: 'primary' | 'secondary'): void {
    const policy = this.form.controls[group];
    if (policy.disabled) return;
    const self = policy.controls.relationshipToPatient.value === 'Self';
    for (const name of ['subscriberName', 'subscriberDateOfBirth'] as const) {
      const field = policy.controls[name];
      if (self) field.disable({ emitEvent: false });
      else field.enable({ emitEvent: false });
    }
    policy.updateValueAndValidity();
  }
}

/** Text messages need SMS consent; email needs an address. Mirrors the API's rules. */
function contactRules(control: AbstractControl): ValidationErrors | null {
  const form = control as FormGroup;
  const method = form.get('preferredContactMethod')?.value as ContactMethod;
  if (method === 'Sms' && !form.get('smsOptIn')?.value) return { smsConsentRequired: true };
  if (method === 'Email' && !String(form.get('email')?.value ?? '').trim()) return { emailRequired: true };
  return null;
}
