import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PortalInvoiceList, PortalInvoiceSummary } from '../../core/models/portal.models';
import { PortalApiService } from '../../core/services/portal.service';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { MoneyPipe } from '../../shared/money.pipe';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { PortalContext } from './portal-context';

/** /p/:slug/invoices — the patient's issued invoices and what they still owe. */
@Component({
  selector: 'app-portal-invoices',
  standalone: true,
  imports: [RouterLink, NzCardModule, NzIconModule, NzTagModule, DateOnlyPipe, MoneyPipe, PageStateComponent],
  template: `
    <h1>Invoices</h1>
    <app-page-state [status]="invoices.status()" (retry)="invoices.reload()" errorText="We could not load your invoices."
                    [empty]="!invoices.value().items.length" emptyText="You have no invoices.">
      @if (invoices.value().outstanding.length) {
        <nz-card class="owed">
          <span class="muted">You owe</span>
          @for (o of invoices.value().outstanding; track o.currency) { <strong>{{ o.amount | money: o.currency }}</strong> }
          <small class="muted">Please contact the clinic to pay.</small>
        </nz-card>
      }
      <div class="list">
        @for (invoice of invoices.value().items; track invoice.id) {
          <a class="invoice" [routerLink]="context.link('invoices', invoice.id)">
            <span>
              <strong>{{ invoice.number }}</strong>
              <small class="muted">{{ invoice.issueDate | dateOnly }}</small>
            </span>
            <span class="right">
              <strong>{{ invoice.total | money: invoice.currency }}</strong>
              <nz-tag [nzColor]="tag(invoice).color">{{ tag(invoice).label }}</nz-tag>
            </span>
          </a>
        }
      </div>
    </app-page-state>
  `,
  styles: [`
    h1 { font-size: 24px; margin: 0 0 var(--da-space-4); }
    .muted { color: var(--da-ink-3); }
    .owed { margin-bottom: var(--da-space-3); }
    .owed ::ng-deep .ant-card-body { display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--da-space-2); }
    .owed strong { font-size: 22px; }
    .list { display: grid; gap: var(--da-space-2); }
    .invoice { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-3); padding: var(--da-space-3) var(--da-space-4);
      background: var(--da-surface); border: 1px solid var(--da-border); border-radius: var(--da-radius); color: var(--da-ink); }
    .invoice:hover { border-color: var(--da-brand); }
    .invoice span { display: grid; gap: 2px; }
    .right { justify-items: end; }
    .right nz-tag { margin: 0; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalInvoicesComponent {
  readonly context = inject(PortalContext);
  private readonly api = inject(PortalApiService);

  readonly invoices = remoteData(() => this.api.invoices(1, 100),
    { items: [], totalCount: 0, page: 1, pageSize: 100, outstanding: [] } as PortalInvoiceList);

  tag(invoice: PortalInvoiceSummary): { label: string; color: string } {
    if (invoice.status === 'Paid') return { label: 'Paid', color: 'green' };
    if (invoice.isOverdue) return { label: 'Overdue', color: 'red' };
    return invoice.status === 'PartiallyPaid' ? { label: 'Partly paid', color: 'gold' } : { label: 'Due', color: 'blue' };
  }
}
