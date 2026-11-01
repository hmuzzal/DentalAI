import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormGroup, FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { catchError, debounceTime, distinctUntilChanged, finalize, map, of, Subject, switchMap } from 'rxjs';
import { Invoice, InvoiceLine } from '../../core/models/billing.models';
import { Patient } from '../../core/models/patient.models';
import { DentalService } from '../../core/models/service.models';
import { BillingSettingsService } from '../../core/services/billing-settings.service';
import { InvoicesService } from '../../core/services/invoices.service';
import { PatientsService } from '../../core/services/patients.service';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { optional, saveErrorText, touchAll } from '../../shared/forms';
import { formatMoney } from '../../shared/money.pipe';

/**
 * Create or edit a draft invoice. Pass `invoice` (a draft) to edit, or `patient` to start one for a patient.
 * The totals shown are a preview; the server's totals (rounded to the invoice currency) are the ones kept.
 */
@Component({
  selector: 'app-invoice-editor-drawer',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, NzButtonModule, NzFormModule, NzIconModule, NzInputModule, NzInputNumberModule, NzSelectModule, FormDrawerComponent],
  template: `
    <app-form-drawer [open]="open()" [title]="invoice() ? 'Edit draft invoice' : 'New invoice'" [width]="880"
                     [submitLabel]="invoice() ? 'Save draft' : 'Create draft'" [saving]="saving()" [error]="error()"
                     (closed)="closed.emit()" (submitted)="save()">
      <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
        <nz-form-item>
          <nz-form-label nzRequired nzFor="invoice-patient">Patient</nz-form-label>
          <nz-form-control nzErrorTip="Choose the patient.">
            @if (invoice()) {
              <input nz-input id="invoice-patient" [value]="invoice()!.patientName" readonly />
            } @else {
              <nz-select nzId="invoice-patient" formControlName="patientId" nzShowSearch nzServerSearch nzAllowClear
                         nzPlaceHolder="Search patients by name, phone or email" (nzOnSearch)="patientSearch.next($event)"
                         [nzLoading]="searchingPatients()">
                @for (p of patientOptions(); track p.id) {
                  <nz-option [nzValue]="p.id" [nzLabel]="p.fullName + (p.phone ? ' · ' + p.phone : '')" />
                }
              </nz-select>
            }
          </nz-form-control>
        </nz-form-item>

        <div class="lines" formArrayName="lines">
          <div class="lines__head" aria-hidden="true">
            <span>Service / description</span><span>Code</span><span>Tooth</span><span>Qty</span>
            <span>Unit price</span><span>Insurance est.</span><span class="right">Line total</span><span></span>
          </div>
          @for (line of lines.controls; track line; let i = $index) {
            <div class="line" [formGroupName]="i">
              <div class="line__desc">
                <nz-select formControlName="serviceId" nzShowSearch nzAllowClear nzPlaceHolder="Service (optional)"
                           nzSize="small" [attr.aria-label]="'Line ' + (i + 1) + ' service'">
                  @for (s of services(); track s.id) {
                    <nz-option [nzValue]="s.id" [nzLabel]="s.name" />
                  }
                </nz-select>
                <input nz-input nzSize="small" formControlName="description" placeholder="Description" maxlength="300"
                       [attr.aria-label]="'Line ' + (i + 1) + ' description'" />
              </div>
              <input nz-input nzSize="small" formControlName="procedureCode" placeholder="D1110" maxlength="20"
                     [attr.aria-label]="'Line ' + (i + 1) + ' procedure code'" />
              <input nz-input nzSize="small" formControlName="tooth" [placeholder]="toothHint()" maxlength="20"
                     [attr.aria-label]="'Line ' + (i + 1) + ' tooth'" />
              <nz-input-number nzSize="small" formControlName="quantity" [nzMin]="1" [nzMax]="1000" [nzPrecision]="0"
                               [attr.aria-label]="'Line ' + (i + 1) + ' quantity'" />
              <nz-input-number nzSize="small" formControlName="unitPrice" [nzMin]="0" [nzPrecision]="decimals()"
                               [attr.aria-label]="'Line ' + (i + 1) + ' unit price'" />
              <nz-input-number nzSize="small" formControlName="insuranceEstimate" [nzMin]="0" [nzPrecision]="decimals()"
                               [attr.aria-label]="'Line ' + (i + 1) + ' insurance estimate'" />
              <span class="right num">{{ money(lineTotal(i)) }}</span>
              <button nz-button nzType="text" nzSize="small" type="button" (click)="removeLine(i)" [disabled]="lines.length === 1"
                      [attr.aria-label]="'Remove line ' + (i + 1)"><span nz-icon nzType="close"></span></button>
              @if (lineError(i); as problem) { <div class="line__error" role="alert">{{ problem }}</div> }
            </div>
          }
          <button nz-button nzType="dashed" type="button" (click)="addLine()" class="add"><span nz-icon nzType="plus"></span> Add line</button>
        </div>

        <div class="bottom">
          <div class="options">
            <nz-form-item>
              <nz-form-label nzFor="invoice-coverage">Insurance covers</nz-form-label>
              <nz-form-control nzExtra="Fills every line's insurance estimate with this share. You can still edit each line.">
                <div class="inline">
                  <nz-input-number nzId="invoice-coverage" [nzMin]="0" [nzMax]="100" [nzPrecision]="0" nzPlaceHolder="%"
                                   [(ngModel)]="coverage" [ngModelOptions]="{ standalone: true }" />
                  <span>%</span>
                  <button nz-button nzSize="small" type="button" (click)="applyCoverage()" [disabled]="coverage === null">Apply</button>
                </div>
              </nz-form-control>
            </nz-form-item>
            <div class="row">
              <nz-form-item>
                <nz-form-label nzFor="invoice-discount">Discount ({{ currency() }})</nz-form-label>
                <nz-form-control>
                  <nz-input-number nzId="invoice-discount" formControlName="discountAmount" [nzMin]="0" [nzPrecision]="decimals()" class="full" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzFor="invoice-tax">Tax rate (%)</nz-form-label>
                <nz-form-control>
                  <nz-input-number nzId="invoice-tax" formControlName="taxRatePercent" [nzMin]="0" [nzMax]="100" [nzPrecision]="3" class="full" />
                </nz-form-control>
              </nz-form-item>
            </div>
            <nz-form-item>
              <nz-form-label nzFor="invoice-notes">Notes</nz-form-label>
              <nz-form-control>
                <textarea nz-input id="invoice-notes" rows="2" formControlName="notes" maxlength="2000"
                          placeholder="Shown on the invoice"></textarea>
              </nz-form-control>
            </nz-form-item>
          </div>

          <dl class="totals" aria-label="Totals preview">
            <dt>Subtotal</dt><dd>{{ money(preview().subtotal) }}</dd>
            @if (preview().discount) { <dt>Discount</dt><dd>−{{ money(preview().discount) }}</dd> }
            @if (preview().tax) { <dt>Tax</dt><dd>{{ money(preview().tax) }}</dd> }
            <dt class="strong">Total</dt><dd class="strong">{{ money(preview().total) }}</dd>
            @if (preview().insurance) {
              <dt>Insurance estimate</dt><dd>−{{ money(preview().insurance) }}</dd>
              <dt class="strong">Patient pays</dt><dd class="strong">{{ money(preview().total - preview().insurance) }}</dd>
            }
          </dl>
        </div>
        <button type="submit" hidden></button>
      </form>
    </app-form-drawer>
  `,
  styles: [`
    .lines { border: 1px solid var(--da-border); border-radius: var(--da-radius); padding: var(--da-space-2); margin-bottom: var(--da-space-3); }
    .lines__head, .line { display: grid; grid-template-columns: minmax(200px, 2.4fr) 90px 70px 70px 110px 110px 100px 32px; gap: var(--da-space-2); align-items: start; }
    .lines__head { font-size: 12px; color: var(--da-ink-3); padding: 0 0 var(--da-space-1); }
    .line { padding: var(--da-space-1) 0; border-top: 1px solid var(--da-border); }
    .line__desc { display: grid; gap: 4px; }
    .line nz-input-number { width: 100%; }
    .line > .num { padding-top: 4px; }
    .line__error { grid-column: 1 / -1; color: var(--da-danger); font-size: 12px; }
    .right { text-align: right; }
    .num { font-variant-numeric: tabular-nums; }
    .add { margin-top: var(--da-space-2); }
    .bottom { display: grid; grid-template-columns: 1fr 280px; gap: var(--da-space-4); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .inline { display: flex; align-items: center; gap: var(--da-space-2); }
    .full { width: 100%; }
    .totals { display: grid; grid-template-columns: 1fr auto; gap: 6px var(--da-space-3); margin: 0; align-self: start;
      padding: var(--da-space-3); background: var(--da-surface-2); border-radius: var(--da-radius); font-variant-numeric: tabular-nums; }
    .totals dt { color: var(--da-ink-2); }
    .totals dd { margin: 0; text-align: right; }
    .totals .strong { color: var(--da-ink); font-weight: 600; }
    @media (max-width: 860px) {
      .lines__head { display: none; }
      .line { grid-template-columns: 1fr 1fr 1fr; }
      .line__desc { grid-column: 1 / -1; }
      .bottom { grid-template-columns: 1fr; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InvoiceEditorDrawerComponent {
  readonly open = input(false);
  /** A draft to edit; null to create. */
  readonly invoice = input<Invoice | null>(null);
  /** Pre-selects the patient for a new invoice. */
  readonly patient = input<{ id: string; fullName: string; phone?: string | null } | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Invoice>();

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly invoices = inject(InvoicesService);
  private readonly patients = inject(PatientsService);
  private readonly billing = inject(BillingSettingsService);
  private readonly messages = inject(NzMessageService);

  readonly services = toSignal(inject(ServicesCatalogService).getAll().pipe(
    map((list) => list.filter((s) => s.isActive)), catchError(() => of([] as DentalService[]))), { initialValue: [] as DentalService[] });

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly patientOptions = signal<Pick<Patient, 'id' | 'fullName' | 'phone'>[]>([]);
  readonly searchingPatients = signal(false);
  readonly patientSearch = new Subject<string>();
  coverage: number | null = null;

  readonly form = this.fb.group({
    patientId: ['', Validators.required],
    lines: this.fb.array<FormGroup>([]),
    discountAmount: [0, [Validators.min(0)]],
    taxRatePercent: [0, [Validators.min(0), Validators.max(100)]],
    notes: ['']
  });
  get lines(): FormArray<FormGroup> {
    return this.form.controls.lines;
  }

  /** Bumped on every change so the preview recalculates. */
  private readonly version = signal(0);

  /** The currency of the draft being edited, or the clinic's for a new one. */
  readonly currency = computed(() => this.invoice()?.currency ?? this.billing.currency());
  readonly decimals = computed(() => this.invoice()?.currencyDecimals ?? this.billing.settings()?.currencyDecimals ?? 2);
  readonly toothHint = computed(() => (this.billing.settings()?.toothNumbering === 'Fdi' ? '26' : '14'));

  readonly preview = computed(() => {
    this.version();
    const round = (n: number) => Number(n.toFixed(this.decimals()));
    const lines = this.lines.getRawValue() as unknown as InvoiceLine[];
    const subtotal = round(lines.reduce((sum, l) => sum + round((Number(l.quantity) || 0) * (Number(l.unitPrice) || 0)), 0));
    const discount = Math.min(Number(this.form.controls.discountAmount.value) || 0, subtotal);
    const tax = round((subtotal - discount) * (Number(this.form.controls.taxRatePercent.value) || 0) / 100);
    const total = subtotal - discount + tax;
    const insurance = Math.min(lines.reduce((sum, l) => sum + (Number(l.insuranceEstimate) || 0), 0), total);
    return { subtotal, discount, tax, total, insurance };
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.form.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => this.version.update((v) => v + 1));
    this.patientSearch.pipe(
      debounceTime(250), distinctUntilChanged(),
      switchMap((text) => {
        this.searchingPatients.set(true);
        return this.patients.search({ search: text, page: 1, pageSize: 20 }).pipe(
          map((page) => page.items), catchError(() => of([] as Patient[])), finalize(() => this.searchingPatients.set(false)));
      }),
      takeUntilDestroyed(destroyRef)
    ).subscribe((items) => this.patientOptions.set(items));

    effect(() => {
      if (!this.open()) return;
      const invoice = this.invoice();
      const patient = this.patient();
      const defaultTax = this.billing.settings()?.defaultTaxRatePercent ?? 0;
      untracked(() => this.fill(invoice, patient, defaultTax));
    });
  }

  money(value: number): string {
    return formatMoney(value, this.currency());
  }

  lineTotal(index: number): number {
    this.version();
    const line = this.lines.at(index).getRawValue() as unknown as InvoiceLine;
    return (Number(line.quantity) || 0) * (Number(line.unitPrice) || 0);
  }

  lineError(index: number): string | null {
    this.version();
    const line = this.lines.at(index);
    if (!line.touched && !line.dirty) return null;
    const v = line.getRawValue() as unknown as InvoiceLine;
    if (!String(v.description ?? '').trim()) return 'Enter a description (or pick a service).';
    if ((Number(v.insuranceEstimate) || 0) > this.lineTotal(index)) return "The insurance estimate can't be more than the line total.";
    return null;
  }

  addLine(line?: Partial<InvoiceLine>): void {
    const group = this.fb.group({
      serviceId: [line?.serviceId ?? null],
      description: [line?.description ?? '', [Validators.required, Validators.maxLength(300)]],
      procedureCode: [line?.procedureCode ?? ''],
      tooth: [line?.tooth ?? ''],
      quantity: [line?.quantity ?? 1, [Validators.required, Validators.min(1)]],
      unitPrice: [line?.unitPrice ?? 0, [Validators.required, Validators.min(0)]],
      insuranceEstimate: [line?.insuranceEstimate ?? 0, [Validators.min(0)]]
    });
    // Picking a service fills the description and price (both still editable).
    group.controls.serviceId.valueChanges.subscribe((id) => this.pickService(group, id));
    this.lines.push(group);
  }

  removeLine(index: number): void {
    if (this.lines.length > 1) this.lines.removeAt(index);
  }

  private pickService(line: FormGroup, serviceId: string | null): void {
    const service = this.services().find((s) => s.id === serviceId);
    if (service) line.patchValue({ description: service.name, unitPrice: service.price });
  }

  applyCoverage(): void {
    if (this.coverage === null) return;
    const share = this.coverage / 100;
    this.lines.controls.forEach((_, i) =>
      this.lines.at(i).patchValue({ insuranceEstimate: Number((this.lineTotal(i) * share).toFixed(this.decimals())) } as never));
  }

  save(): void {
    if (this.saving()) return;
    const badLine = this.lines.controls.some((_, i) => {
      this.lines.at(i).markAsTouched();
      return this.lineError(i) !== null;
    });
    if (this.form.invalid || badLine) {
      touchAll(this.form);
      this.lines.controls.forEach((line) => touchAll(line));
      this.version.update((v) => v + 1);
      return;
    }

    const v = this.form.getRawValue();
    const lines = (v.lines as unknown as InvoiceLine[]).map((l) => ({
      serviceId: l.serviceId || null,
      description: String(l.description).trim(),
      procedureCode: optional(l.procedureCode),
      tooth: optional(l.tooth),
      quantity: Number(l.quantity),
      unitPrice: Number(l.unitPrice),
      insuranceEstimate: Number(l.insuranceEstimate) || 0
    }));
    const draft = { lines, discountAmount: Number(v.discountAmount) || 0, taxRatePercent: Number(v.taxRatePercent) || 0, notes: optional(v.notes) };
    const existing = this.invoice();
    const call = existing
      ? this.invoices.updateDraft(existing.id, draft)
      : this.invoices.create({ ...draft, patientId: v.patientId });

    this.saving.set(true);
    this.error.set(null);
    call.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (invoice) => {
        this.messages.success(existing ? 'Draft saved.' : 'Draft invoice created.');
        this.saved.emit(invoice);
      },
      error: (error: unknown) => this.error.set(saveErrorText(error))
    });
  }

  private fill(invoice: Invoice | null, patient: { id: string; fullName: string; phone?: string | null } | null, defaultTax: number): void {
    this.error.set(null);
    this.coverage = null;
    this.lines.clear();
    if (invoice) {
      invoice.lines.forEach((line) => this.addLine(line));
    } else {
      this.addLine();
    }
    this.patientOptions.set(patient ? [{ id: patient.id, fullName: patient.fullName, phone: patient.phone ?? '' }] : []);
    this.form.patchValue({
      patientId: invoice?.patientId ?? patient?.id ?? '',
      discountAmount: invoice?.discountAmount ?? 0,
      taxRatePercent: invoice?.taxRatePercent ?? defaultTax,
      notes: invoice?.notes ?? ''
    });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    if (!invoice && !patient) this.patientSearch.next('');
  }
}
