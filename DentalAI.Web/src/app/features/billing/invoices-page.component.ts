import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { catchError, debounceTime, distinctUntilChanged, finalize, map, of, Subject, switchMap, tap } from 'rxjs';
import { INVOICE_STATUS, Invoice, InvoiceList, InvoiceListQuery, InvoiceStatus, InvoiceSummary } from '../../core/models/billing.models';
import { InvoicesService } from '../../core/services/invoices.service';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { formatMoney, MoneyPipe } from '../../shared/money.pipe';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { LoadStatus } from '../../shared/remote-data';
import { InvoiceEditorDrawerComponent } from './invoice-editor-drawer.component';

type Filter = 'All' | 'Drafts' | 'Unpaid' | 'Paid' | 'Void';
const FILTERS: Filter[] = ['All', 'Drafts', 'Unpaid', 'Paid', 'Void'];
const EMPTY: InvoiceList = { items: [], totalCount: 0, page: 1, pageSize: 20, outstanding: [] };

interface Query {
  filter: Filter;
  search: string;
  patientId: string | null;
  page: number;
  pageSize: number;
  version: number;
}

/** /invoices: the clinic's invoices. ?patient=<id> shows one patient's; ?filter=Unpaid etc. */
@Component({
  selector: 'app-invoices-page',
  standalone: true,
  imports: [
    DateOnlyPipe, FormsModule, NzButtonModule, NzCardModule, NzIconModule, NzInputModule, NzSegmentedModule, NzTableModule,
    NzTagModule, MoneyPipe, PageHeaderComponent, PageStateComponent, InvoiceEditorDrawerComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Invoices" [subtitle]="outstandingText()">
        <button nz-button nzType="primary" (click)="editorOpen.set(true)">
          <span nz-icon nzType="plus"></span> New invoice
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="status()" (retry)="reload()" [rows]="6" errorText="We could not load invoices.">
          <div class="toolbar">
            <nz-segmented [nzOptions]="filters" [ngModel]="query().filter" (ngModelChange)="setFilter($event)" aria-label="Show" />
            <nz-input-group class="search" [nzPrefix]="searchIcon">
              <input nz-input type="search" placeholder="Invoice number or patient" aria-label="Search invoices"
                     [ngModel]="searchText()" (ngModelChange)="onSearch($event)" />
            </nz-input-group>
            <ng-template #searchIcon><span nz-icon nzType="search"></span></ng-template>
            @if (query().patientId) {
              <nz-tag nzMode="closeable" (nzOnClose)="clearPatient()">Patient: {{ patientName() || '…' }}</nz-tag>
            }
            <span class="count">{{ result().totalCount }} invoice{{ result().totalCount === 1 ? '' : 's' }}</span>
          </div>

          <nz-table [nzData]="result().items" [nzFrontPagination]="false" [nzLoading]="fetching()" [nzTotal]="result().totalCount"
                    [nzPageIndex]="query().page" [nzPageSize]="query().pageSize" (nzPageIndexChange)="setPage($event)"
                    [nzHideOnSinglePage]="true" nzSize="middle" [nzNoResult]="none">
            <thead>
              <tr>
                <th>Invoice</th>
                <th>Patient</th>
                <th>Issued</th>
                <th>Due</th>
                <th nzAlign="right">Total</th>
                <th nzAlign="right">Balance</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (invoice of result().items; track invoice.id) {
                <tr class="row" tabindex="0" (click)="open(invoice)" (keydown.enter)="open(invoice)"
                    [attr.aria-label]="(invoice.number ?? 'Draft') + ' for ' + invoice.patientName">
                  <td class="nowrap"><strong>{{ invoice.number ?? 'Draft' }}</strong></td>
                  <td>{{ invoice.patientName }}</td>
                  <td class="num nowrap">{{ invoice.issueDate ? (invoice.issueDate | dateOnly: 'MMM d, yyyy') : '—' }}</td>
                  <td class="num nowrap">
                    {{ invoice.dueDate ? (invoice.dueDate | dateOnly: 'MMM d, yyyy') : '—' }}
                    @if (invoice.isOverdue) { <nz-tag nzColor="red">Overdue</nz-tag> }
                  </td>
                  <td nzAlign="right" class="num">{{ invoice.total | money: invoice.currency }}</td>
                  <td nzAlign="right" class="num">
                    @if (invoice.status === 'Draft' || invoice.status === 'Void') { — }
                    @else { {{ invoice.balance | money: invoice.currency }} }
                  </td>
                  <td><nz-tag [nzColor]="statuses[invoice.status].color">{{ statuses[invoice.status].label }}</nz-tag></td>
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #none>
            <div class="none">
              <p>{{ query().search || query().filter !== 'All' || query().patientId ? 'No invoices match.' : 'No invoices yet.' }}</p>
              <p class="muted">Create one here, or from a completed appointment.</p>
            </div>
          </ng-template>
        </app-page-state>
      </nz-card>

      <app-invoice-editor-drawer [open]="editorOpen()" [patient]="presetPatient()"
                                 (closed)="editorOpen.set(false)" (saved)="onCreated($event)" />
    </section>
  `,
  styles: [`
    .toolbar { display: flex; align-items: center; gap: var(--da-space-3); flex-wrap: wrap; margin-bottom: var(--da-space-3); }
    .search { max-width: 300px; flex: 1 1 220px; }
    .count { margin-left: auto; color: var(--da-ink-2); }
    .row { cursor: pointer; }
    .row:focus-visible { outline: 2px solid var(--da-brand); outline-offset: -2px; }
    .num { font-variant-numeric: tabular-nums; }
    .nowrap { white-space: nowrap; }
    .none { text-align: center; padding: var(--da-space-4) 0; }
    .muted { color: var(--da-ink-2); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InvoicesPageComponent {
  private readonly invoices = inject(InvoicesService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly statuses = INVOICE_STATUS;
  readonly filters = FILTERS;
  private readonly params = this.route.snapshot.queryParamMap;
  readonly query = signal<Query>({
    filter: FILTERS.includes(this.params.get('filter') as Filter) ? (this.params.get('filter') as Filter) : 'All',
    search: this.params.get('search') ?? '',
    patientId: this.params.get('patient'),
    page: Number(this.params.get('page')) || 1,
    pageSize: 20,
    version: 0
  });
  readonly searchText = signal(this.query().search);
  readonly result = signal<InvoiceList>(EMPTY);
  readonly status = signal<LoadStatus>('loading');
  readonly fetching = signal(false);
  readonly editorOpen = signal(false);
  /** The patient's name for the filter chip (from the first result). */
  readonly patientName = computed(() => this.result().items[0]?.patientName ?? null);
  readonly presetPatient = computed(() => {
    const id = this.query().patientId;
    const name = this.patientName();
    return id && name ? { id, fullName: name } : null;
  });

  readonly outstandingText = computed(() => {
    const owed = this.result().outstanding.filter((x) => x.amount > 0);
    return owed.length
      ? `Outstanding: ${owed.map((x) => formatMoney(x.amount, x.currency)).join(' + ')}`
      : 'Nothing outstanding.';
  });

  private readonly searchInput = new Subject<string>();

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.searchInput.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(destroyRef))
      .subscribe((search) => this.query.update((q) => ({ ...q, search: search.trim(), page: 1 })));

    toObservable(this.query).pipe(
      tap((q) => this.syncUrl(q)),
      tap(() => this.fetching.set(true)),
      switchMap((q) => this.invoices.list(toApiQuery(q)).pipe(
        map((list) => ({ ok: true as const, list })),
        catchError(() => of({ ok: false as const })),
        finalize(() => this.fetching.set(false))
      )),
      takeUntilDestroyed(destroyRef)
    ).subscribe((outcome) => {
      if (!outcome.ok) {
        if (this.status() === 'loading') this.status.set('error');
        return;
      }
      this.result.set(outcome.list);
      this.status.set('ready');
    });
  }

  setFilter(filter: Filter): void {
    this.query.update((q) => ({ ...q, filter, page: 1 }));
  }

  onSearch(text: string): void {
    this.searchText.set(text);
    this.searchInput.next(text);
  }

  setPage(page: number): void {
    this.query.update((q) => (q.page === page ? q : { ...q, page }));
  }

  clearPatient(): void {
    this.query.update((q) => ({ ...q, patientId: null, page: 1 }));
  }

  reload(): void {
    this.status.set('loading');
    this.query.update((q) => ({ ...q, version: q.version + 1 }));
  }

  open(invoice: InvoiceSummary): void {
    void this.router.navigate(['/invoices', invoice.id]);
  }

  onCreated(invoice: Invoice): void {
    this.editorOpen.set(false);
    void this.router.navigate(['/invoices', invoice.id]);
  }

  private syncUrl(q: Query): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        filter: q.filter === 'All' ? null : q.filter,
        search: q.search || null,
        patient: q.patientId,
        page: q.page > 1 ? q.page : null
      }
    });
  }
}

function toApiQuery(q: Query): InvoiceListQuery {
  const status: Partial<Record<Filter, InvoiceStatus>> = { Drafts: 'Draft', Paid: 'Paid', Void: 'Void' };
  return {
    status: status[q.filter] ?? null,
    outstandingOnly: q.filter === 'Unpaid',
    patientId: q.patientId,
    search: q.search,
    page: q.page,
    pageSize: q.pageSize
  };
}
