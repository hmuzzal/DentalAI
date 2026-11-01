import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { INVOICE_STATUS, InvoiceDocument, PAYMENT_METHOD_LABELS } from '../../core/models/billing.models';
import { InvoicesService } from '../../core/services/invoices.service';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { MoneyPipe } from '../../shared/money.pipe';
import { PhonePipe } from '../../shared/phone.pipe';

/**
 * /invoices/:id/print : the invoice as a document, outside the app layout. "Print / Save as PDF" uses the browser's
 * print dialog; the print stylesheet hides the toolbar.
 */
@Component({
  selector: 'app-invoice-print-page',
  standalone: true,
  imports: [DatePipe, DateOnlyPipe, MoneyPipe, PhonePipe],
  template: `
    <div class="toolbar no-print">
      <button type="button" (click)="print()" [disabled]="!doc()">Print / Save as PDF</button>
      <button type="button" class="secondary" (click)="close()">Close</button>
    </div>

    @if (error()) {
      <p class="paper">We could not load this invoice.</p>
    } @else if (doc(); as d) {
      @let inv = d.invoice;
      <article class="paper">
        <header class="head">
          <div>
            <h1>{{ d.clinic.name }}</h1>
            @if (d.clinic.addressLine1) { <div>{{ d.clinic.addressLine1 }}</div> }
            @if (d.clinic.phone) { <div>{{ d.clinic.phone | phone }}</div> }
            @if (d.clinic.email) { <div>{{ d.clinic.email }}</div> }
          </div>
          <div class="right">
            <h2>{{ inv.status === 'Draft' ? 'DRAFT INVOICE' : 'INVOICE' }}</h2>
            @if (inv.number) { <div><strong>{{ inv.number }}</strong></div> }
            @if (inv.issueDate) { <div>Issued {{ inv.issueDate | dateOnly: 'MMM d, yyyy' }}</div> }
            @if (inv.dueDate) { <div>Due {{ inv.dueDate | dateOnly: 'MMM d, yyyy' }}</div> }
            @if (inv.status === 'Void') { <div class="stamp">VOID</div> }
            @else if (inv.status === 'Paid') { <div class="stamp paid">PAID</div> }
          </div>
        </header>

        <section class="bill-to">
          <div class="label">Bill to</div>
          <strong>{{ d.patient.name }}</strong>
          @if (d.patient.addressLine1) { <div>{{ d.patient.addressLine1 }}</div> }
          @if (d.patient.addressLine2) { <div>{{ d.patient.addressLine2 }}</div> }
          @if (d.patient.cityLine) { <div>{{ d.patient.cityLine }}</div> }
          @if (d.patient.phone) { <div>{{ d.patient.phone | phone }}</div> }
        </section>

        <table class="lines">
          <thead>
            <tr>
              <th>Description</th>
              <th>Code</th>
              <th>Tooth</th>
              <th class="num">Qty</th>
              <th class="num">Unit price</th>
              <th class="num">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (line of inv.lines; track line.id) {
              <tr>
                <td>{{ line.description }}</td>
                <td>{{ line.procedureCode ?? '' }}</td>
                <td>{{ line.tooth ?? '' }}</td>
                <td class="num">{{ line.quantity }}</td>
                <td class="num">{{ line.unitPrice | money: inv.currency }}</td>
                <td class="num">{{ line.lineTotal ?? 0 | money: inv.currency }}</td>
              </tr>
            }
          </tbody>
        </table>

        <table class="totals">
          <tr><td>Subtotal</td><td class="num">{{ inv.subtotal | money: inv.currency }}</td></tr>
          @if (inv.discountAmount) { <tr><td>Discount</td><td class="num">−{{ inv.discountAmount | money: inv.currency }}</td></tr> }
          @if (inv.taxAmount) { <tr><td>Tax ({{ inv.taxRatePercent }}%)</td><td class="num">{{ inv.taxAmount | money: inv.currency }}</td></tr> }
          <tr class="strong"><td>Total</td><td class="num">{{ inv.total | money: inv.currency }}</td></tr>
          @if (inv.insuranceEstimate) {
            <tr><td>Insurance estimate</td><td class="num">{{ inv.insuranceEstimate | money: inv.currency }}</td></tr>
            <tr><td>Patient's share (estimate)</td><td class="num">{{ inv.patientResponsibility | money: inv.currency }}</td></tr>
          }
          @for (p of inv.payments; track p.id) {
            <tr><td>Paid {{ p.receivedAtUtc | date: 'MMM d' }} · {{ methods[p.method] }}</td><td class="num">−{{ p.amount | money: inv.currency }}</td></tr>
            @for (r of p.refunds; track r.id) {
              <tr><td>Refunded {{ r.refundedAtUtc | date: 'MMM d' }}</td><td class="num">{{ r.amount | money: inv.currency }}</td></tr>
            }
          }
          <tr class="strong due"><td>Balance due</td><td class="num">{{ inv.balance | money: inv.currency }}</td></tr>
        </table>

        @if (inv.notes) { <p class="notes">{{ inv.notes }}</p> }
        @if (inv.insuranceEstimate) {
          <p class="fine">Insurance amounts are estimates. You are responsible for any amount your insurance does not pay.</p>
        }
        <footer class="fine">Tooth numbers use the {{ d.toothNumbering === 'Fdi' ? 'FDI' : 'Universal' }} system. Status: {{ statuses[inv.status].label }}.</footer>
      </article>
    } @else {
      <p class="paper">Loading…</p>
    }
  `,
  styles: [`
    :host { display: block; background: #f3f4f6; min-height: 100vh; padding: 24px 12px; color: #111; }
    .toolbar { max-width: 800px; margin: 0 auto 12px; display: flex; gap: 8px; justify-content: flex-end; }
    .toolbar button { font: inherit; padding: 8px 14px; border-radius: 6px; border: 1px solid #1e5660; background: #1e5660; color: #fff; cursor: pointer; }
    .toolbar .secondary { background: #fff; color: #1e5660; }
    .paper { max-width: 800px; margin: 0 auto; background: #fff; padding: 48px; box-shadow: 0 2px 12px rgba(0, 0, 0, .08); font-size: 14px; line-height: 1.5; }
    .head { display: flex; justify-content: space-between; gap: 24px; margin-bottom: 32px; }
    h1 { font-size: 22px; margin: 0 0 4px; }
    h2 { font-size: 20px; letter-spacing: .08em; margin: 0 0 4px; color: #1e5660; }
    .right { text-align: right; }
    .stamp { display: inline-block; margin-top: 8px; padding: 2px 10px; border: 2px solid #b42318; color: #b42318; font-weight: 700; letter-spacing: .1em; }
    .stamp.paid { border-color: #067647; color: #067647; }
    .bill-to { margin-bottom: 24px; }
    .label { font-size: 12px; text-transform: uppercase; letter-spacing: .08em; color: #555; }
    table { width: 100%; border-collapse: collapse; }
    .lines th, .lines td { padding: 8px 6px; border-bottom: 1px solid #e5e7eb; text-align: left; vertical-align: top; }
    .lines th { font-size: 12px; text-transform: uppercase; letter-spacing: .05em; color: #555; border-bottom: 2px solid #111; }
    .num { text-align: right !important; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .totals { width: 320px; margin: 16px 0 0 auto; }
    .totals td { padding: 4px 6px; }
    .totals .strong td { font-weight: 700; border-top: 1px solid #111; }
    .totals .due td { font-size: 16px; }
    .notes { margin-top: 24px; white-space: pre-line; }
    .fine { margin-top: 16px; font-size: 12px; color: #555; }
    @media print {
      :host { background: #fff; padding: 0; }
      .no-print { display: none !important; }
      .paper { box-shadow: none; padding: 0; max-width: none; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InvoicePrintPageComponent {
  readonly statuses = INVOICE_STATUS;
  readonly methods = PAYMENT_METHOD_LABELS;
  readonly doc = signal<InvoiceDocument | null>(null);
  readonly error = signal(false);

  constructor() {
    const id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
    inject(InvoicesService).document(id).subscribe({
      next: (doc) => {
        this.doc.set(doc);
        document.title = `${doc.invoice.number ?? 'Draft invoice'} · ${doc.patient.name}`;
      },
      error: () => this.error.set(true)
    });
  }

  print(): void {
    window.print();
  }

  close(): void {
    window.close();
  }
}
