import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { catchError, finalize, of } from 'rxjs';
import { BillingSettings, CurrencyOption, TOOTH_NUMBERING_LABELS, ToothNumbering } from '../../core/models/billing.models';
import { BillingSettingsService } from '../../core/services/billing-settings.service';
import { saveErrorText, touchAll } from '../../shared/forms';
import { formatMoney } from '../../shared/money.pipe';
import { PageHeaderComponent } from '../../shared/page-header.component';

/** /settings/billing (clinic admins): currency, invoice numbering, payment terms, tax and tooth numbering. */
@Component({
  selector: 'app-billing-settings-page',
  standalone: true,
  imports: [
    ReactiveFormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzFormModule, NzInputModule, NzInputNumberModule,
    NzRadioModule, NzSelectModule, NzSkeletonModule, PageHeaderComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Billing settings" eyebrow="Settings"
                       subtitle="How this clinic's invoices are numbered, priced and shown." />

      <nz-card>
        @if (!loaded()) {
          <nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 8 }" />
        } @else {
          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()" class="form">
            <nz-form-item>
              <nz-form-label nzRequired nzFor="billing-currency">Currency</nz-form-label>
              <nz-form-control nzErrorTip="Choose a currency."
                               nzExtra="Prices and new invoices use this currency. Existing invoices keep the currency they were created in.">
                <nz-select nzId="billing-currency" formControlName="currency" nzShowSearch nzPlaceHolder="Search currencies"
                           [nzLoading]="!currencies().length">
                  @for (c of currencies(); track c.code) {
                    <nz-option [nzValue]="c.code" [nzLabel]="c.code + ' · ' + c.name" />
                  }
                </nz-select>
              </nz-form-control>
            </nz-form-item>
            @if (currencyChanged()) {
              <nz-alert nzType="warning" nzShowIcon class="gap"
                        nzMessage="Changing the currency doesn't convert any amounts."
                        nzDescription="Service prices stay the same numbers, now read in the new currency. Check your prices after saving." />
            }

            <div class="row">
              <nz-form-item>
                <nz-form-label nzRequired nzFor="billing-prefix">Invoice number prefix</nz-form-label>
                <nz-form-control nzErrorTip="Letters and numbers only, up to 10." [nzExtra]="'Next invoice: ' + nextNumber()">
                  <input nz-input id="billing-prefix" formControlName="invoicePrefix" maxlength="10" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzRequired nzFor="billing-terms">Payment due (days after issue)</nz-form-label>
                <nz-form-control nzErrorTip="Between 0 and 365 days." nzExtra="0 means due on receipt.">
                  <nz-input-number nzId="billing-terms" formControlName="paymentTermsDays" [nzMin]="0" [nzMax]="365" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
            </div>

            <nz-form-item>
              <nz-form-label nzFor="billing-tax">Default tax rate (%)</nz-form-label>
              <nz-form-control nzErrorTip="Between 0 and 100." nzExtra="Applied to new invoices. In the US, dental services are generally not taxed.">
                <nz-input-number nzId="billing-tax" formControlName="defaultTaxRatePercent" [nzMin]="0" [nzMax]="100" [nzStep]="0.5" [nzPrecision]="3" class="narrow" />
              </nz-form-control>
            </nz-form-item>

            <nz-form-item>
              <nz-form-label>Tooth numbering</nz-form-label>
              <nz-form-control nzExtra="Used for the tooth on invoice lines (and charts later).">
                <nz-radio-group formControlName="toothNumbering" class="stack">
                  @for (option of numberingOptions; track option) {
                    <label nz-radio [nzValue]="option">{{ numberingLabels[option] }}</label>
                  }
                </nz-radio-group>
              </nz-form-control>
            </nz-form-item>

            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <div class="actions">
              <button nz-button nzType="primary" [nzLoading]="saving()" [disabled]="form.pristine">Save settings</button>
            </div>
          </form>
        }
      </nz-card>
    </section>
  `,
  styles: [`
    .form { max-width: 640px; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .full { width: 100%; }
    .narrow { width: 160px; }
    .stack { display: grid; gap: var(--da-space-2); }
    .gap { margin-bottom: var(--da-space-3); }
    .actions { display: flex; justify-content: flex-end; }
    @media (max-width: 640px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BillingSettingsPageComponent {
  private readonly billing = inject(BillingSettingsService);
  private readonly messages = inject(NzMessageService);

  readonly numberingOptions: ToothNumbering[] = ['Universal', 'Fdi'];
  readonly numberingLabels = TOOTH_NUMBERING_LABELS;
  readonly currencies = toSignal(this.billing.currencies().pipe(catchError(() => of([] as CurrencyOption[]))), {
    initialValue: [] as CurrencyOption[]
  });

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  private readonly saved = signal<BillingSettings | null>(null);
  readonly loaded = computed(() => this.saved() !== null);

  readonly form = inject(NonNullableFormBuilder).group({
    currency: ['USD', Validators.required],
    invoicePrefix: ['INV', [Validators.required, Validators.maxLength(10), Validators.pattern(/^[A-Za-z0-9]+$/)]],
    paymentTermsDays: [30, [Validators.required, Validators.min(0), Validators.max(365)]],
    defaultTaxRatePercent: [0, [Validators.min(0), Validators.max(100)]],
    toothNumbering: ['Universal' as ToothNumbering]
  });

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  readonly currencyChanged = computed(() => {
    const saved = this.saved();
    return !!saved && this.values().currency !== saved.currency;
  });
  readonly nextNumber = computed(() => {
    const prefix = (this.values().invoicePrefix || 'INV').toUpperCase();
    return `${prefix}-${String(this.saved()?.nextInvoiceNumber ?? 1).padStart(6, '0')}`;
  });

  constructor() {
    this.billing.load().subscribe({
      next: (settings) => this.fill(settings),
      error: () => this.error.set('We could not load the billing settings.')
    });
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.billing
      .update({ ...v, invoicePrefix: v.invoicePrefix.trim().toUpperCase(), defaultTaxRatePercent: v.defaultTaxRatePercent ?? 0 })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (settings) => {
          this.fill(settings);
          this.messages.success(`Saved. Prices now show as ${formatMoney(1234.5, settings.currency)}.`);
        },
        error: (error: unknown) => this.error.set(saveErrorText(error))
      });
  }

  private fill(settings: BillingSettings): void {
    this.saved.set(settings);
    this.form.reset({
      currency: settings.currency,
      invoicePrefix: settings.invoicePrefix,
      paymentTermsDays: settings.paymentTermsDays,
      defaultTaxRatePercent: settings.defaultTaxRatePercent,
      toothNumbering: settings.toothNumbering
    });
  }
}
