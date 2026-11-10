import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PAYMENT_METHOD_LABELS } from '../../core/models/billing.models';
import { PortalInvoice } from '../../core/models/portal.models';
import { PortalApiService } from '../../core/services/portal.service';
import { formatInZone } from '../../core/time-zone';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { MoneyPipe } from '../../shared/money.pipe';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { PortalContext } from './portal-context';

/** /p/:slug/invoices/:id — one invoice: lines, totals, insurance estimate and payments. Printable. */
@Component({
  selector: 'app-portal-invoice',
  standalone: true,
  imports: [RouterLink, NzButtonModule, NzCardModule, NzIconModule, NzTagModule, DateOnlyPipe, MoneyPipe, PageStateComponent],
  template: `
    <div class="no-print">
      <a nz-button nzType="link" class="back" [routerLink]="context.link('invoices')"><span nz-icon nzType="left"></span>Invoices</a>
    </div>
    <app-page-state [status]="invoice.status()" (retry)="invoice.reload()" errorText="We could not load this invoice.">
      @if (invoice.value(); as i) {
        <nz-card>
          <div class="top">
            <div>
              <h1>Invoice {{ i.number }}</h1>
              <div class="muted">{{ context.clinic()?.name }}</div>
              <div class="muted">Issued {{ i.issueDate | dateOnly }}@if (i.dueDate) { · Due {{ i.dueDate | dateOnly }} }</div>
            </div>
            <button nz-button class="no-print" (click)="print()"><span nz-icon nzType="printer"></span>Print</button>
          </div>

          <table class="lines">
            <thead><tr><th>Item</th><th class="num">Qty</th><th class="num">Amount</th></tr></thead>
            <tbody>
              @for (line of i.lines; track $index) {
                <tr>
                  <td>{{ line.description }}@if (line.tooth) { <small class="muted"> · tooth {{ line.tooth }}</small> }</td>
                  <td class="num">{{ line.quantity }}</td>
                  <td class="num">{{ line.lineTotal | money: i.currency }}</td>
                </tr>
              }
            </tbody>
          </table>

          <dl class="totals">
            <dt>Subtotal</dt><dd>{{ i.subtotal | money: i.currency }}</dd>
            @if (i.discountAmount) { <dt>Discount</dt><dd>−{{ i.discountAmount | money: i.currency }}</dd> }
            @if (i.taxAmount) { <dt>Tax ({{ i.taxRatePercent }}%)</dt><dd>{{ i.taxAmount | money: i.currency }}</dd> }
            <dt class="strong">Total</dt><dd class="strong">{{ i.total | money: i.currency }}</dd>
            @if (i.insuranceEstimate) {
              <dt>Insurance estimate</dt><dd>−{{ i.insuranceEstimate | money: i.currency }}</dd>
              <dt>Your estimated share</dt><dd>{{ i.patientResponsibility | money: i.currency }}</dd>
            }
            <dt>Paid</dt><dd>{{ i.amountPaid | money: i.currency }}</dd>
            <dt class="strong">Balance</dt><dd class="strong">{{ i.balance | money: i.currency }}</dd>
          </dl>

          @if (i.payments.length) {
            <h2>Payments</h2>
            <ul class="payments">
              @for (p of i.payments; track $index) {
                <li>
                  <span>{{ day(p.receivedAtUtc) }} · {{ methods[p.method] }}</span>
                  <span>{{ p.amount | money: i.currency }}@if (p.refundedAmount) { <small class="muted"> ({{ p.refundedAmount | money: i.currency }} refunded)</small> }</span>
                </li>
              }
            </ul>
          }
          @if (i.notes) { <p class="notes">{{ i.notes }}</p> }
          @if (i.balance > 0) { <p class="muted no-print">To pay, please contact the clinic.</p> }
        </nz-card>
      }
    </app-page-state>
  `,
  styles: [`
    .back { padding-left: 0; }
    .top { display: flex; justify-content: space-between; gap: var(--da-space-3); align-items: flex-start; }
    h1 { font-size: 22px; margin: 0; }
    h2 { font-size: 15px; margin: var(--da-space-4) 0 var(--da-space-2); }
    .muted { color: var(--da-ink-3); }
    .lines { width: 100%; border-collapse: collapse; margin-top: var(--da-space-4); }
    .lines th { text-align: left; font-size: 12px; color: var(--da-ink-3); font-weight: 600; padding: var(--da-space-2) 0;
      border-bottom: 1px solid var(--da-border); }
    .lines td { padding: var(--da-space-2) 0; border-bottom: 1px solid var(--da-border); vertical-align: top; }
    .num { text-align: right !important; font-variant-numeric: tabular-nums; white-space: nowrap; padding-left: var(--da-space-3) !important; }
    .totals { display: grid; grid-template-columns: 1fr auto; gap: var(--da-space-1) var(--da-space-4); margin: var(--da-space-3) 0 0 auto;
      max-width: 320px; font-variant-numeric: tabular-nums; }
    .totals dd { margin: 0; text-align: right; }
    .totals dt { color: var(--da-ink-2); }
    .strong { font-weight: 600; color: var(--da-ink) !important; }
    .payments { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--da-space-1); }
    .payments li { display: flex; justify-content: space-between; gap: var(--da-space-3); }
    .notes { white-space: pre-line; margin-top: var(--da-space-4); color: var(--da-ink-2); }
    @media print { .no-print { display: none !important; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalInvoiceComponent {
  readonly context = inject(PortalContext);
  private readonly api = inject(PortalApiService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;

  readonly methods = PAYMENT_METHOD_LABELS;
  readonly invoice = remoteData(() => this.api.invoice(this.id), null as PortalInvoice | null);

  day(utc: string): string {
    return formatInZone(utc, this.context.zone(), { month: 'short', day: 'numeric', year: 'numeric' });
  }

  print(): void {
    window.print();
  }
}
