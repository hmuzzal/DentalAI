import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { finalize, Observable } from 'rxjs';
import { INVOICE_STATUS, Invoice, PAYMENT_METHOD_LABELS, Payment, PaymentMethod } from '../../core/models/billing.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { InvoicesService } from '../../core/services/invoices.service';
import { saveErrorText } from '../../shared/forms';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { formatMoney, MoneyPipe } from '../../shared/money.pipe';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { InvoiceEditorDrawerComponent } from './invoice-editor-drawer.component';

type Dialog = 'payment' | 'refund' | 'void' | null;

/** /invoices/:id */
@Component({
  selector: 'app-invoice-page',
  standalone: true,
  imports: [
    DatePipe, DateOnlyPipe, FormsModule, RouterLink, NzAlertModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzFormModule,
    NzIconModule, NzInputModule, NzInputNumberModule, NzModalModule, NzPopconfirmModule, NzSelectModule, NzSkeletonModule,
    NzTableModule, NzTagModule, MoneyPipe, PageHeaderComponent, InvoiceEditorDrawerComponent
  ],
  template: `
    <section class="da-page">
      @if (!invoice()) {
        @if (loadError()) {
          <nz-alert nzType="error" nzShowIcon nzMessage="We could not load this invoice." [nzAction]="back" />
          <ng-template #back><a nz-button nzSize="small" routerLink="/invoices">Back to invoices</a></ng-template>
        } @else {
          <nz-card><nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 10 }" /></nz-card>
        }
      } @else {
        @let inv = invoice()!;
        <app-page-header [title]="inv.number ? 'Invoice ' + inv.number : 'Draft invoice'" eyebrow="Billing"
                         [subtitle]="inv.patientName">
          <a nz-button routerLink="/invoices"><span nz-icon nzType="left"></span> Invoices</a>
          @if (inv.status === 'Draft') {
            <button nz-button (click)="editorOpen.set(true)">Edit</button>
            <button nz-button nzDanger nz-popconfirm nzPopconfirmTitle="Delete this draft?" nzOkText="Delete" nzOkDanger
                    (nzOnConfirm)="deleteDraft()" [nzLoading]="busy()">Delete</button>
            <button nz-button nzType="primary" nz-popconfirm
                    nzPopconfirmTitle="Issue this invoice? It gets a number and can no longer be edited."
                    nzOkText="Issue" (nzOnConfirm)="issue()" [nzLoading]="busy()">Issue invoice</button>
          } @else {
            <a nz-button [routerLink]="['/invoices', inv.id, 'print']" target="_blank"><span nz-icon nzType="printer"></span> Print</a>
            @if (isAdmin() && (inv.status === 'Issued')) {
              <button nz-button nzDanger (click)="openDialog('void')">Void</button>
            }
            @if (inv.status === 'Issued' || inv.status === 'PartiallyPaid') {
              <button nz-button nzType="primary" (click)="openDialog('payment')">Record payment</button>
            }
          }
        </app-page-header>

        <div class="layout">
          <div class="main">
            <nz-card>
              <div class="meta">
                <div><span class="label">Status</span>
                  <nz-tag [nzColor]="statuses[inv.status].color">{{ statuses[inv.status].label }}</nz-tag>
                  @if (inv.isOverdue) { <nz-tag nzColor="red">Overdue</nz-tag> }
                </div>
                <div><span class="label">Patient</span>
                  <a [routerLink]="['/invoices']" [queryParams]="{ patient: inv.patientId }">{{ inv.patientName }}</a>
                </div>
                <div><span class="label">Issued</span>{{ inv.issueDate ? (inv.issueDate | dateOnly: 'MMM d, yyyy') : 'Not yet' }}</div>
                <div><span class="label">Due</span>{{ inv.dueDate ? (inv.dueDate | dateOnly: 'MMM d, yyyy') : '—' }}</div>
                <div><span class="label">Currency</span>{{ inv.currency }}</div>
              </div>
              @if (inv.status === 'Void') {
                <nz-alert nzType="warning" nzShowIcon [nzMessage]="'Voided: ' + (inv.voidReason ?? '')" class="gap" />
              }

              <nz-table [nzData]="inv.lines" [nzFrontPagination]="false" [nzShowPagination]="false" nzSize="small" class="gap">
                <thead>
                  <tr>
                    <th>Description</th><th>Code</th><th>Tooth</th>
                    <th nzAlign="right">Qty</th><th nzAlign="right">Unit price</th>
                    <th nzAlign="right">Insurance est.</th><th nzAlign="right">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  @for (line of inv.lines; track line.id) {
                    <tr>
                      <td>{{ line.description }}</td>
                      <td>{{ line.procedureCode || '—' }}</td>
                      <td>{{ line.tooth || '—' }}</td>
                      <td nzAlign="right" class="num">{{ line.quantity }}</td>
                      <td nzAlign="right" class="num">{{ line.unitPrice | money: inv.currency }}</td>
                      <td nzAlign="right" class="num">{{ line.insuranceEstimate ? (line.insuranceEstimate | money: inv.currency) : '—' }}</td>
                      <td nzAlign="right" class="num">{{ line.lineTotal ?? 0 | money: inv.currency }}</td>
                    </tr>
                  }
                </tbody>
              </nz-table>
              @if (inv.notes) { <p class="notes"><span class="label">Notes</span>{{ inv.notes }}</p> }
            </nz-card>

            <nz-card nzTitle="Payments">
              @if (!inv.payments.length) {
                <p class="muted">No payments yet.</p>
              } @else {
                <nz-table [nzData]="inv.payments" [nzFrontPagination]="false" [nzShowPagination]="false" nzSize="small">
                  <thead>
                    <tr><th>Date</th><th>Method</th><th>Reference</th><th nzAlign="right">Amount</th><th></th></tr>
                  </thead>
                  <tbody>
                    @for (p of inv.payments; track p.id) {
                      <tr>
                        <td class="nowrap">{{ p.receivedAtUtc | date: 'MMM d, yyyy, h:mm a' }}</td>
                        <td>{{ methods[p.method] }}</td>
                        <td>{{ p.reference || '—' }}@if (p.note) { <div class="muted">{{ p.note }}</div> }</td>
                        <td nzAlign="right" class="num">{{ p.amount | money: inv.currency }}</td>
                        <td nzAlign="right">
                          @if (isAdmin() && p.amount > p.refundedAmount && inv.status !== 'Void') {
                            <button nz-button nzType="link" nzSize="small" (click)="openDialog('refund', p)">Refund</button>
                          }
                        </td>
                      </tr>
                      @for (r of p.refunds; track r.id) {
                        <tr class="refund">
                          <td class="nowrap">{{ r.refundedAtUtc | date: 'MMM d, yyyy, h:mm a' }}</td>
                          <td>Refund</td>
                          <td>{{ r.reason }}</td>
                          <td nzAlign="right" class="num">−{{ r.amount | money: inv.currency }}</td>
                          <td></td>
                        </tr>
                      }
                    }
                  </tbody>
                </nz-table>
              }
            </nz-card>
          </div>

          <nz-card class="totals-card">
            <dl class="totals">
              <dt>Subtotal</dt><dd>{{ inv.subtotal | money: inv.currency }}</dd>
              @if (inv.discountAmount) { <dt>Discount</dt><dd>−{{ inv.discountAmount | money: inv.currency }}</dd> }
              @if (inv.taxAmount) { <dt>Tax ({{ inv.taxRatePercent }}%)</dt><dd>{{ inv.taxAmount | money: inv.currency }}</dd> }
              <dt class="strong">Total</dt><dd class="strong">{{ inv.total | money: inv.currency }}</dd>
              @if (inv.insuranceEstimate) {
                <dt>Insurance estimate</dt><dd>{{ inv.insuranceEstimate | money: inv.currency }}</dd>
                <dt>Patient's share</dt><dd>{{ inv.patientResponsibility | money: inv.currency }}</dd>
              }
              <dt>Paid</dt><dd>{{ inv.amountPaid | money: inv.currency }}</dd>
              <dt class="strong big">Balance</dt><dd class="strong big">{{ inv.balance | money: inv.currency }}</dd>
            </dl>
            @if (inv.status === 'Draft') {
              <p class="muted small">Issue the invoice to give it a number and take payments.</p>
            }
          </nz-card>
        </div>

        <app-invoice-editor-drawer [open]="editorOpen()" [invoice]="inv" (closed)="editorOpen.set(false)" (saved)="onEdited($event)" />

        <!-- Record payment -->
        <nz-modal [nzVisible]="dialog() === 'payment'" nzTitle="Record payment" (nzOnCancel)="closeDialog()" [nzFooter]="paymentFooter">
          <ng-container *nzModalContent>
            <form nz-form nzLayout="vertical" (ngSubmit)="recordPayment()">
              <div class="row">
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="pay-amount">Amount ({{ inv.currency }})</nz-form-label>
                  <nz-form-control [nzExtra]="'Balance: ' + money(inv.balance)">
                    <nz-input-number nzId="pay-amount" name="amount" [(ngModel)]="payment.amount" [nzMin]="0"
                                     [nzMax]="inv.balance" [nzPrecision]="inv.currencyDecimals" class="full" />
                  </nz-form-control>
                </nz-form-item>
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="pay-method">Method</nz-form-label>
                  <nz-form-control>
                    <nz-select nzId="pay-method" name="method" [(ngModel)]="payment.method">
                      @for (m of paymentMethods; track m) { <nz-option [nzValue]="m" [nzLabel]="methods[m]" /> }
                    </nz-select>
                  </nz-form-control>
                </nz-form-item>
              </div>
              <div class="row">
                <nz-form-item>
                  <nz-form-label nzFor="pay-ref">{{ payment.method === 'Check' ? 'Check number' : payment.method === 'Card' ? 'Card last 4' : 'Reference' }}</nz-form-label>
                  <nz-form-control>
                    <input nz-input id="pay-ref" name="reference" [(ngModel)]="payment.reference" maxlength="100" />
                  </nz-form-control>
                </nz-form-item>
                <nz-form-item>
                  <nz-form-label nzFor="pay-date">Received</nz-form-label>
                  <nz-form-control>
                    <nz-date-picker nzId="pay-date" name="received" [(ngModel)]="payment.receivedAt" [nzShowTime]="{ nzFormat: 'h:mm a', nzUse12Hours: true }"
                                    nzFormat="MMM d, yyyy h:mm a" [nzDisabledDate]="isFuture" [nzAllowClear]="false" class="full" />
                  </nz-form-control>
                </nz-form-item>
              </div>
              <nz-form-item>
                <nz-form-label nzFor="pay-note">Note</nz-form-label>
                <nz-form-control>
                  <input nz-input id="pay-note" name="note" [(ngModel)]="payment.note" maxlength="500" />
                </nz-form-control>
              </nz-form-item>
              @if (dialogError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="dialogError()!" /> }
              <button type="submit" hidden></button>
            </form>
          </ng-container>
          <ng-template #paymentFooter>
            <button nz-button (click)="closeDialog()">Cancel</button>
            <button nz-button nzType="primary" [nzLoading]="busy()" (click)="recordPayment()"
                    [disabled]="!payment.amount || payment.amount <= 0">Record {{ money(payment.amount || 0) }}</button>
          </ng-template>
        </nz-modal>

        <!-- Refund -->
        <nz-modal [nzVisible]="dialog() === 'refund'" nzTitle="Refund payment" (nzOnCancel)="closeDialog()" [nzFooter]="refundFooter">
          <ng-container *nzModalContent>
            @if (refundTarget(); as p) {
              <p>Refunding from the {{ methods[p.method].toLowerCase() }} payment of {{ money(p.amount) }}
                on {{ p.receivedAtUtc | date: 'MMM d' }}. Give the money back the same way.</p>
              <form nz-form nzLayout="vertical" (ngSubmit)="refund()">
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="refund-amount">Amount ({{ inv.currency }})</nz-form-label>
                  <nz-form-control [nzExtra]="'Up to ' + money(p.amount - p.refundedAmount)">
                    <nz-input-number nzId="refund-amount" name="amount" [(ngModel)]="refundAmount" [nzMin]="0"
                                     [nzMax]="p.amount - p.refundedAmount" [nzPrecision]="inv.currencyDecimals" class="full" />
                  </nz-form-control>
                </nz-form-item>
                <nz-form-item>
                  <nz-form-label nzRequired nzFor="refund-reason">Reason</nz-form-label>
                  <nz-form-control>
                    <input nz-input id="refund-reason" name="reason" [(ngModel)]="reason" maxlength="500" />
                  </nz-form-control>
                </nz-form-item>
                @if (dialogError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="dialogError()!" /> }
                <button type="submit" hidden></button>
              </form>
            }
          </ng-container>
          <ng-template #refundFooter>
            <button nz-button (click)="closeDialog()">Cancel</button>
            <button nz-button nzType="primary" nzDanger [nzLoading]="busy()" (click)="refund()"
                    [disabled]="!refundAmount || !reason.trim()">Refund {{ money(refundAmount || 0) }}</button>
          </ng-template>
        </nz-modal>

        <!-- Void -->
        <nz-modal [nzVisible]="dialog() === 'void'" nzTitle="Void invoice" (nzOnCancel)="closeDialog()" [nzFooter]="voidFooter">
          <ng-container *nzModalContent>
            <p>Voiding cancels invoice {{ inv.number }}. It stays on record but can't be paid. This can't be undone.</p>
            <form nz-form nzLayout="vertical" (ngSubmit)="voidInvoice()">
              <nz-form-item>
                <nz-form-label nzRequired nzFor="void-reason">Reason</nz-form-label>
                <nz-form-control>
                  <input nz-input id="void-reason" name="reason" [(ngModel)]="reason" maxlength="500" placeholder="e.g. Billed in error" />
                </nz-form-control>
              </nz-form-item>
              @if (dialogError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="dialogError()!" /> }
              <button type="submit" hidden></button>
            </form>
          </ng-container>
          <ng-template #voidFooter>
            <button nz-button (click)="closeDialog()">Cancel</button>
            <button nz-button nzType="primary" nzDanger [nzLoading]="busy()" (click)="voidInvoice()" [disabled]="!reason.trim()">Void invoice</button>
          </ng-template>
        </nz-modal>
      }
    </section>
  `,
  styles: [`
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 300px; gap: var(--da-space-4); align-items: start; }
    .main { display: grid; gap: var(--da-space-4); min-width: 0; }
    .meta { display: flex; flex-wrap: wrap; gap: var(--da-space-2) var(--da-space-5); }
    .label { display: block; font-size: 12px; color: var(--da-ink-3); margin-bottom: 2px; }
    .gap { margin-top: var(--da-space-3); }
    .notes { margin: var(--da-space-3) 0 0; white-space: pre-line; }
    .num { font-variant-numeric: tabular-nums; }
    .nowrap { white-space: nowrap; }
    .muted { color: var(--da-ink-2); }
    .small { font-size: 13px; margin: var(--da-space-3) 0 0; }
    .refund td { color: var(--da-ink-2); font-size: 13px; }
    .totals-card { position: sticky; top: var(--da-space-3); }
    .totals { display: grid; grid-template-columns: 1fr auto; gap: 8px var(--da-space-3); margin: 0; font-variant-numeric: tabular-nums; }
    .totals dt { color: var(--da-ink-2); }
    .totals dd { margin: 0; text-align: right; }
    .totals .strong { color: var(--da-ink); font-weight: 600; }
    .totals .big { font-size: 18px; padding-top: var(--da-space-2); border-top: 1px solid var(--da-border); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .full { width: 100%; }
    @media (max-width: 1000px) { .layout { grid-template-columns: 1fr; } .totals-card { position: static; } }
    @media (max-width: 520px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InvoicePageComponent {
  private readonly invoices = inject(InvoicesService);
  private readonly router = inject(Router);
  private readonly messages = inject(NzMessageService);
  private readonly auth = inject(AuthStateService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  readonly statuses = INVOICE_STATUS;
  readonly methods = PAYMENT_METHOD_LABELS;
  readonly paymentMethods: PaymentMethod[] = ['Cash', 'Card', 'Check', 'Insurance', 'Other'];
  readonly isAdmin = computed(() => this.auth.role() === 'ClinicAdmin');
  readonly isFuture = (date: Date) => date > new Date();

  readonly invoice = signal<Invoice | null>(null);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  readonly editorOpen = signal(false);
  readonly dialog = signal<Dialog>(null);
  readonly dialogError = signal<string | null>(null);
  readonly refundTarget = signal<Payment | null>(null);

  payment = { amount: 0 as number | null, method: 'Cash' as PaymentMethod, reference: '', receivedAt: new Date(), note: '' };
  refundAmount: number | null = null;
  reason = '';

  constructor() {
    this.load();
  }

  money(value: number): string {
    return formatMoney(value, this.invoice()?.currency ?? 'USD');
  }

  load(): void {
    this.invoices.get(this.id).subscribe({
      next: (invoice) => this.invoice.set(invoice),
      error: () => this.loadError.set(true)
    });
  }

  onEdited(invoice: Invoice): void {
    this.editorOpen.set(false);
    this.invoice.set(invoice);
  }

  issue(): void {
    this.run(this.invoices.issue(this.id), (inv) => `Invoice ${inv.number} issued.`);
  }

  deleteDraft(): void {
    this.busy.set(true);
    this.invoices.deleteDraft(this.id).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.messages.success('Draft deleted.');
        void this.router.navigate(['/invoices']);
      }
    });
  }

  openDialog(kind: Exclude<Dialog, null>, target?: Payment): void {
    const inv = this.invoice();
    if (!inv) return;
    this.dialogError.set(null);
    this.reason = '';
    if (kind === 'payment') {
      this.payment = { amount: inv.balance, method: 'Cash', reference: '', receivedAt: new Date(), note: '' };
    }
    if (kind === 'refund' && target) {
      this.refundTarget.set(target);
      this.refundAmount = target.amount - target.refundedAmount;
    }
    this.dialog.set(kind);
  }

  closeDialog(): void {
    if (!this.busy()) this.dialog.set(null);
  }

  recordPayment(): void {
    const p = this.payment;
    if (!p.amount || p.amount <= 0) return;
    this.runDialog(this.invoices.recordPayment(this.id, {
      amount: p.amount,
      method: p.method,
      reference: p.reference.trim() || null,
      receivedAtUtc: p.receivedAt.toISOString(),
      note: p.note.trim() || null
    }), () => `Payment of ${this.money(p.amount ?? 0)} recorded.`);
  }

  refund(): void {
    const target = this.refundTarget();
    if (!target || !this.refundAmount || !this.reason.trim()) return;
    const amount = this.refundAmount;
    this.runDialog(this.invoices.refund(this.id, target.id, amount, this.reason.trim()), () => `Refund of ${this.money(amount)} recorded.`);
  }

  voidInvoice(): void {
    if (!this.reason.trim()) return;
    this.runDialog(this.invoices.void(this.id, this.reason.trim()), (inv) => `Invoice ${inv.number} voided.`);
  }

  private run(call: Observable<Invoice>, message: (inv: Invoice) => string): void {
    this.busy.set(true);
    call.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (inv) => {
        this.invoice.set(inv);
        this.messages.success(message(inv));
      }
    });
  }

  private runDialog(call: Observable<Invoice>, message: (inv: Invoice) => string): void {
    this.busy.set(true);
    this.dialogError.set(null);
    call.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (inv) => {
        this.invoice.set(inv);
        this.dialog.set(null);
        this.messages.success(message(inv));
      },
      error: (error: unknown) => {
        this.dialogError.set(saveErrorText(error));
        this.load(); // the balance may have changed (another payment)
      }
    });
  }
}
