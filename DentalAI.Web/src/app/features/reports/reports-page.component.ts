import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { addDays, differenceInCalendarDays, format, parseISO, startOfMonth, startOfYear, subMonths, endOfMonth } from 'date-fns';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule } from 'ng-zorro-antd/table';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { PAYMENT_METHOD_LABELS } from '../../core/models/billing.models';
import { AGING_COLUMNS, PracticeReport, ReceivablesReport, ReportKpis } from '../../core/models/report.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { ReportsService } from '../../core/services/reports.service';
import { dateKeyInZone, effectiveTimeZone } from '../../core/time-zone';
import { downloadCsv } from '../../shared/csv';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { formatMoney, MoneyPipe } from '../../shared/money.pipe';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { LoadStatus } from '../../shared/remote-data';
import { RevenueBar, RevenueChartComponent } from '../dashboard/revenue-chart.component';

type Tab = 'Practice' | 'Receivables';
type Preset = 'this-month' | 'last-month' | 'last-90' | 'this-year' | 'custom';
const MAX_DAYS = 366;
const KEY = 'yyyy-MM-dd';

interface Tile {
  label: string;
  value: string;
  change: string | null;
  help: string;
}

/** /reports: production, collections, visits and patients for a date range; receivables aging. Clinic staff. */
@Component({
  selector: 'app-reports-page',
  standalone: true,
  imports: [
    DateOnlyPipe, FormsModule, RouterLink, NzAlertModule, NzButtonModule, NzCardModule, NzDatePickerModule, NzIconModule,
    NzSegmentedModule, NzSelectModule, NzTableModule, MoneyPipe, PageHeaderComponent, PageStateComponent, RevenueChartComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Reports" [subtitle]="subtitle()">
        <nz-segmented [nzOptions]="tabs" [ngModel]="tab()" (ngModelChange)="setTab($event)" aria-label="Report" />
      </app-page-header>

      @if (tab() === 'Practice') {
        <div class="filters" role="group" aria-label="Date range">
          <nz-select [ngModel]="preset()" (ngModelChange)="applyPreset($event)" class="preset" nzAriaLabel="Range">
            @for (p of presets; track p.value) { <nz-option [nzValue]="p.value" [nzLabel]="p.label" /> }
          </nz-select>
          <nz-range-picker [ngModel]="pickerValue()" (ngModelChange)="pickRange($event)" [nzAllowClear]="false"
                           nzFormat="MMM d, yyyy" aria-label="From and to dates" />
          @if (rangeError()) { <span class="error" role="alert">{{ rangeError() }}</span> }
        </div>

        <app-page-state [status]="status()" (retry)="reload()" [rows]="8" errorText="We could not load the report.">
          @if (report(); as r) {
            @if (r.otherCurrencyInvoices) {
              <nz-alert nzType="info" nzShowIcon class="gap"
                        [nzMessage]="r.otherCurrencyInvoices + ' invoice(s) in this range use another currency and are not included in the money figures.'" />
            }

            <div class="kpis">
              @for (tile of tiles(); track tile.label) {
                <div class="kpi" [title]="tile.help">
                  <span class="kpi__label">{{ tile.label }}</span>
                  <strong class="kpi__value">{{ tile.value }}</strong>
                  <span class="kpi__change">{{ tile.change ?? 'No earlier data' }}</span>
                </div>
              }
            </div>
            <p class="muted small">Compared with {{ r.previousFrom | dateOnly: 'MMM d' }} – {{ r.previousTo | dateOnly: 'MMM d, yyyy' }}.
              @if (r.includesMoney) {
                Production is invoiced work (after discounts, before tax) by issue date; collections are payments received minus refunds.
              } @else {
                Money figures are for clinic admins.
              }</p>

            <div class="charts" [class.charts--single]="!r.includesMoney">
              @if (r.includesMoney) {
                <nz-card [nzTitle]="'Production by ' + unit()" nzSize="small">
                  <app-revenue-chart [bars]="bars().production" [currency]="r.currency" [labelEvery]="labelEvery()" [name]="'Production by ' + unit()" />
                </nz-card>
                <nz-card [nzTitle]="'Collections by ' + unit()" nzSize="small">
                  <app-revenue-chart [bars]="bars().collections" [currency]="r.currency" [labelEvery]="labelEvery()" [name]="'Collections by ' + unit()" />
                </nz-card>
              }
              <nz-card [nzTitle]="'Completed visits by ' + unit()" nzSize="small">
                <app-revenue-chart [bars]="bars().visits" format="count" [labelEvery]="labelEvery()" [name]="'Completed visits by ' + unit()" />
              </nz-card>
            </div>

            <nz-card [nzTitle]="'By ' + unit()" [nzExtra]="trendActions" nzSize="small" class="gap">
              @if (showTrend()) {
                <nz-table [nzData]="r.periods" [nzShowPagination]="false" nzSize="small" [nzScroll]="{ y: '320px' }">
                  <thead>
                    <tr>
                      <th>Period</th>
                      @if (r.includesMoney) { <th nzAlign="right">Production</th><th nzAlign="right">Collections</th> }
                      <th nzAlign="right">Completed visits</th><th nzAlign="right">New patients</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (p of r.periods; track p.start) {
                      <tr>
                        <td>{{ periodLabel(p.start, p.end) }}</td>
                        @if (r.includesMoney) {
                          <td nzAlign="right" class="num">{{ p.production | money: r.currency }}</td>
                          <td nzAlign="right" class="num">{{ p.collections | money: r.currency }}</td>
                        }
                        <td nzAlign="right" class="num">{{ p.completed }}</td>
                        <td nzAlign="right" class="num">{{ p.newPatients }}</td>
                      </tr>
                    }
                  </tbody>
                </nz-table>
              } @else {
                <p class="muted small">The numbers behind the charts.</p>
              }
            </nz-card>
            <ng-template #trendActions>
              <button nz-button nzType="link" nzSize="small" (click)="showTrend.set(!showTrend())">{{ showTrend() ? 'Hide table' : 'Show table' }}</button>
              <button nz-button nzType="link" nzSize="small" (click)="exportTrend(r)"><span nz-icon nzType="download"></span> CSV</button>
            </ng-template>

            <nz-card nzTitle="By doctor" [nzExtra]="doctorCsv" nzSize="small" class="gap">
              <nz-table [nzData]="r.doctors" [nzShowPagination]="false" nzSize="small" [nzNoResult]="nothing">
                <thead>
                  <tr>
                    <th>Doctor</th><th nzAlign="right">Booked</th><th nzAlign="right">Completed</th><th nzAlign="right">Cancelled</th>
                    <th nzAlign="right">No-shows</th>
                    @if (r.includesMoney) { <th nzAlign="right">Production</th><th nzAlign="right">Collections</th> }
                  </tr>
                </thead>
                <tbody>
                  @for (d of r.doctors; track d.doctorId ?? 'none') {
                    <tr>
                      <td [class.muted]="!d.doctorId">{{ d.name }}</td>
                      <td nzAlign="right" class="num">{{ d.booked }}</td>
                      <td nzAlign="right" class="num">{{ d.completed }}</td>
                      <td nzAlign="right" class="num">{{ d.cancelled }}</td>
                      <td nzAlign="right" class="num">{{ d.noShows }}</td>
                      @if (r.includesMoney) {
                        <td nzAlign="right" class="num">{{ d.production | money: r.currency }}</td>
                        <td nzAlign="right" class="num">{{ d.collections | money: r.currency }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </nz-table>
            </nz-card>
            <ng-template #doctorCsv>
              <button nz-button nzType="link" nzSize="small" (click)="exportDoctors(r)" [disabled]="!r.doctors.length"><span nz-icon nzType="download"></span> CSV</button>
            </ng-template>

            <div class="pair" [class.pair--single]="!r.includesMoney">
              <nz-card nzTitle="By service" [nzExtra]="serviceCsv" nzSize="small">
                <nz-table [nzData]="r.services" [nzShowPagination]="false" nzSize="small" [nzNoResult]="nothing" [nzScroll]="{ y: '320px' }">
                  <thead><tr><th>Service</th><th nzAlign="right">Qty</th>@if (r.includesMoney) { <th nzAlign="right">Charged</th> }</tr></thead>
                  <tbody>
                    @for (s of r.services; track s.serviceId ?? s.name) {
                      <tr>
                        <td>{{ s.name }}</td><td nzAlign="right" class="num">{{ s.quantity }}</td>
                        @if (r.includesMoney) { <td nzAlign="right" class="num">{{ s.amount | money: r.currency }}</td> }
                      </tr>
                    }
                  </tbody>
                </nz-table>
                <p class="muted small">{{ r.includesMoney ? 'Invoice lines before invoice discounts and tax.' : 'Services invoiced in this range.' }}</p>
              </nz-card>
              <ng-template #serviceCsv>
                <button nz-button nzType="link" nzSize="small" (click)="exportServices(r)" [disabled]="!r.services.length"><span nz-icon nzType="download"></span> CSV</button>
              </ng-template>

              @if (r.includesMoney) {
                <nz-card nzTitle="By payment method" nzSize="small">
                  <nz-table [nzData]="r.paymentMethods" [nzShowPagination]="false" nzSize="small" [nzNoResult]="nothing">
                    <thead><tr><th>Method</th><th nzAlign="right">Payments</th><th nzAlign="right">Net received</th></tr></thead>
                    <tbody>
                      @for (m of r.paymentMethods; track m.method) {
                        <tr><td>{{ methodLabels[m.method] }}</td><td nzAlign="right" class="num">{{ m.payments }}</td><td nzAlign="right" class="num">{{ m.amount | money: r.currency }}</td></tr>
                      }
                    </tbody>
                  </nz-table>
                  <p class="muted small">Refunds are taken off the method they were paid with, on the day of the refund.</p>
                </nz-card>
              }
            </div>
          }
        </app-page-state>
      } @else {
        <app-page-state [status]="agingStatus()" (retry)="loadAging()" [rows]="6" errorText="We could not load receivables.">
          @if (aging(); as a) {
            @if (a.otherCurrencyInvoices) {
              <nz-alert nzType="info" nzShowIcon class="gap"
                        [nzMessage]="a.otherCurrencyInvoices + ' open invoice(s) use another currency and are not included.'" />
            }
            <div class="kpis kpis--aging">
              <div class="kpi"><span class="kpi__label">Total owed</span><strong class="kpi__value">{{ a.totals.total | money: a.currency }}</strong></div>
              @for (c of agingColumns; track c.key) {
                <div class="kpi"><span class="kpi__label">{{ c.label }}</span><strong class="kpi__value kpi__value--sm">{{ a.totals[c.key] | money: a.currency }}</strong></div>
              }
            </div>
            <p class="muted small">As of {{ a.asOf | dateOnly: 'MMM d, yyyy' }}. Days are counted from each invoice's due date. Most overdue first.</p>

            <nz-card nzTitle="Patients with a balance" [nzExtra]="agingCsv" nzSize="small">
              <nz-table [nzData]="a.patients" [nzPageSize]="25" [nzHideOnSinglePage]="true" nzSize="small" [nzNoResult]="noBalance">
                <thead>
                  <tr>
                    <th>Patient</th><th nzAlign="right">Invoices</th>
                    @for (c of agingColumns; track c.key) { <th nzAlign="right">{{ c.label }}</th> }
                    <th nzAlign="right">Total</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of a.patients; track p.patientId) {
                    <tr>
                      <td><a [routerLink]="['/invoices']" [queryParams]="{ patient: p.patientId, filter: 'Unpaid' }">{{ p.patientName }}</a></td>
                      <td nzAlign="right" class="num">{{ p.openInvoices }}</td>
                      @for (c of agingColumns; track c.key) {
                        <td nzAlign="right" class="num" [class.muted]="!p.buckets[c.key]">{{ p.buckets[c.key] ? (p.buckets[c.key] | money: a.currency) : '—' }}</td>
                      }
                      <td nzAlign="right" class="num"><strong>{{ p.buckets.total | money: a.currency }}</strong></td>
                    </tr>
                  }
                </tbody>
              </nz-table>
            </nz-card>
            <ng-template #agingCsv>
              <button nz-button nzType="link" nzSize="small" (click)="exportAging(a)" [disabled]="!a.patients.length"><span nz-icon nzType="download"></span> CSV</button>
            </ng-template>
          }
        </app-page-state>
      }

      <ng-template #nothing><p class="muted none">Nothing in this range.</p></ng-template>
      <ng-template #noBalance><p class="muted none">No patient owes anything.</p></ng-template>
    </section>
  `,
  styles: [`
    .filters { display: flex; align-items: center; gap: var(--da-space-2); flex-wrap: wrap; margin-bottom: var(--da-space-4); }
    .preset { width: 170px; }
    .error { color: var(--da-danger, #c2410c); }
    .kpis { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: var(--da-space-3); }
    .kpi { display: grid; gap: 2px; padding: var(--da-space-3) var(--da-space-4); background: var(--da-surface); border: 1px solid var(--da-border); border-radius: var(--da-radius); }
    .kpi__label { font-size: 13px; color: var(--da-ink-2); }
    .kpi__value { font-size: 24px; line-height: 32px; font-weight: 600; color: var(--da-ink); font-variant-numeric: tabular-nums; }
    .kpi__value--sm { font-size: 20px; }
    .kpi__change { font-size: 12px; color: var(--da-ink-3); }
    .charts { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--da-space-3); margin-top: var(--da-space-3); }
    .pair { display: grid; grid-template-columns: 3fr 2fr; gap: var(--da-space-3); margin-top: var(--da-space-3); }
    .charts--single, .pair--single { grid-template-columns: minmax(0, 1fr); }
    .gap { margin-top: var(--da-space-3); }
    .muted { color: var(--da-ink-2); }
    .small { font-size: 13px; margin: var(--da-space-2) 0 0; }
    .num { font-variant-numeric: tabular-nums; }
    .none { text-align: center; margin: var(--da-space-3) 0; }
    @media (max-width: 1280px) { .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); } .charts { grid-template-columns: 1fr; } }
    @media (max-width: 900px) { .pair { grid-template-columns: 1fr; } }
    @media (max-width: 560px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportsPageComponent {
  private readonly reports = inject(ReportsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthStateService);

  readonly tabs: Tab[] = ['Practice', 'Receivables'];
  readonly presets: { value: Preset; label: string }[] = [
    { value: 'this-month', label: 'This month' },
    { value: 'last-month', label: 'Last month' },
    { value: 'last-90', label: 'Last 90 days' },
    { value: 'this-year', label: 'This year' },
    { value: 'custom', label: 'Custom' }
  ];
  readonly methodLabels = PAYMENT_METHOD_LABELS;
  readonly agingColumns = AGING_COLUMNS;

  private readonly zone = computed(() => effectiveTimeZone(this.auth.user()?.clinicTimeZone));
  /** Today in the clinic's time zone. */
  private readonly today = computed(() => parseISO(dateKeyInZone(new Date(), this.zone())));

  private readonly params = this.route.snapshot.queryParamMap;
  readonly tab = signal<Tab>(this.params.get('tab') === 'Receivables' ? 'Receivables' : 'Practice');
  readonly range = signal(this.initialRange());
  readonly preset = signal<Preset>(this.matchPreset(this.range()));
  readonly pickerValue = computed(() => [parseISO(this.range().from), parseISO(this.range().to)]);
  readonly rangeError = signal<string | null>(null);

  readonly report = signal<PracticeReport | null>(null);
  readonly status = signal<LoadStatus>('loading');
  private readonly version = signal(0);
  readonly showTrend = signal(false);

  readonly aging = signal<ReceivablesReport | null>(null);
  readonly agingStatus = signal<LoadStatus>('loading');
  private agingRequested = false;

  /** Clinic admins see practice money; the API leaves it out for receptionists. */
  private readonly canSeeMoney = this.auth.hasAnyRole(['ClinicAdmin']);

  readonly subtitle = computed(() => this.tab() === 'Practice'
    ? (this.canSeeMoney ? 'Production, collections, visits and patients' : 'Visits and patients') + ', in the clinic\'s time zone.'
    : 'What patients owe today, by how overdue it is.');

  readonly unit = computed(() => ({ Day: 'day', Week: 'week', Month: 'month' })[this.report()?.granularity ?? 'Day']);
  readonly labelEvery = computed(() => Math.max(1, Math.ceil((this.report()?.periods.length ?? 1) / 8)));

  readonly bars = computed(() => {
    const periods = this.report()?.periods ?? [];
    const label = (start: string) => this.shortLabel(start);
    const bar = (start: string, value: number): RevenueBar => ({ key: start, label: label(start), value });
    return {
      production: periods.map((p) => bar(p.start, p.production ?? 0)),
      collections: periods.map((p) => bar(p.start, p.collections ?? 0)),
      visits: periods.map((p) => bar(p.start, p.completed))
    };
  });

  readonly tiles = computed<Tile[]>(() => {
    const r = this.report();
    if (!r) return [];
    const money = (v: number) => formatMoney(v, r.currency, { wholeUnits: true });
    const pct = (v: number | null) => (v === null ? '—' : `${(v * 100).toFixed(1)}%`);
    const c = r.current;
    const p = r.previous;
    // Without money access (receptionists) the two money tiles become booked visits and patients seen.
    const first: Tile[] = r.includesMoney
      ? [
          { label: 'Production', value: money(c.production ?? 0), change: change(c.production ?? 0, p.production ?? 0), help: 'Invoiced work after discounts, before tax, by issue date.' },
          { label: 'Collections', value: money(c.collections ?? 0), change: change(c.collections ?? 0, p.collections ?? 0), help: 'Payments received minus refunds.' }
        ]
      : [
          { label: 'Booked visits', value: String(c.booked), change: change(c.booked, p.booked), help: 'Appointments in this range, any status.' },
          { label: 'Patients seen', value: String(c.patientsSeen), change: change(c.patientsSeen, p.patientsSeen), help: 'Different patients with a completed visit.' }
        ];
    return [
      ...first,
      { label: 'Completed visits', value: String(c.completed), change: change(c.completed, p.completed), help: `${c.booked} booked in this range.` },
      { label: 'New patients', value: String(c.newPatients), change: change(c.newPatients, p.newPatients), help: 'Patients added to the clinic in this range.' },
      { label: 'No-show rate', value: pct(c.noShowRate), change: points(c.noShowRate, p.noShowRate), help: `${c.noShows} no-show(s) ÷ (completed + no-shows).` },
      { label: 'Cancellation rate', value: pct(c.cancellationRate), change: points(c.cancellationRate, p.cancellationRate), help: `${c.cancelled} cancelled ÷ ${c.booked} booked.` }
    ];
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    toObservable(computed(() => ({ ...this.range(), v: this.version(), tab: this.tab() }))).pipe(
      tap(() => this.syncUrl()),
      switchMap((q) => {
        if (q.tab !== 'Practice') return of(null);
        if (!this.report()) this.status.set('loading');
        return this.reports.practice(q.from, q.to).pipe(map((r) => ({ ok: true as const, r })), catchError(() => of({ ok: false as const })));
      }),
      takeUntilDestroyed(destroyRef)
    ).subscribe((outcome) => {
      if (!outcome) return;
      if (!outcome.ok) {
        this.status.set('error');
        return;
      }
      this.report.set(outcome.r);
      this.status.set('ready');
    });
    if (this.tab() === 'Receivables') this.loadAging();
  }

  setTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'Receivables' && !this.agingRequested) this.loadAging();
  }

  applyPreset(preset: Preset): void {
    this.preset.set(preset);
    if (preset === 'custom') return;
    const today = this.today();
    const ranges: Record<Exclude<Preset, 'custom'>, [Date, Date]> = {
      'this-month': [startOfMonth(today), today],
      'last-month': [startOfMonth(subMonths(today, 1)), endOfMonth(subMonths(today, 1))],
      'last-90': [addDays(today, -89), today],
      'this-year': [startOfYear(today), today]
    };
    const [from, to] = ranges[preset];
    this.setRange(format(from, KEY), format(to, KEY));
  }

  pickRange(value: Date[] | null): void {
    if (!value || value.length !== 2 || !value[0] || !value[1]) return;
    const [from, to] = value;
    if (differenceInCalendarDays(to, from) + 1 > MAX_DAYS) {
      this.rangeError.set(`Choose ${MAX_DAYS} days or fewer.`);
      return;
    }
    this.setRange(format(from, KEY), format(to, KEY));
    this.preset.set(this.matchPreset(this.range()));
  }

  reload(): void {
    this.status.set('loading');
    this.version.update((v) => v + 1);
  }

  loadAging(): void {
    this.agingRequested = true;
    this.agingStatus.set('loading');
    this.reports.receivables().subscribe({
      next: (a) => {
        this.aging.set(a);
        this.agingStatus.set('ready');
      },
      error: () => this.agingStatus.set('error')
    });
  }

  periodLabel(start: string, end: string): string {
    const g = this.report()?.granularity;
    if (g === 'Day') return format(parseISO(start), 'EEE, MMM d');
    if (g === 'Month') return format(parseISO(start), 'MMMM yyyy');
    return start === end ? format(parseISO(start), 'MMM d') : `${format(parseISO(start), 'MMM d')} – ${format(parseISO(end), 'MMM d')}`;
  }

  // Money columns are only exported when the report has them (clinic admins).
  exportTrend(r: PracticeReport): void {
    const money = r.includesMoney ? [`Production (${r.currency})`, `Collections (${r.currency})`] : [];
    downloadCsv(`trend-${r.from}-to-${r.to}.csv`, ['Period start', 'Period end', ...money, 'Completed visits', 'New patients'],
      r.periods.map((p) => [p.start, p.end, ...(r.includesMoney ? [p.production, p.collections] : []), p.completed, p.newPatients]));
  }

  exportDoctors(r: PracticeReport): void {
    const money = r.includesMoney ? [`Production (${r.currency})`, `Collections (${r.currency})`] : [];
    downloadCsv(`doctors-${r.from}-to-${r.to}.csv`, ['Doctor', 'Booked', 'Completed', 'Cancelled', 'No-shows', ...money],
      r.doctors.map((d) => [d.name, d.booked, d.completed, d.cancelled, d.noShows, ...(r.includesMoney ? [d.production, d.collections] : [])]));
  }

  exportServices(r: PracticeReport): void {
    downloadCsv(`services-${r.from}-to-${r.to}.csv`, ['Service', 'Quantity', ...(r.includesMoney ? [`Charged (${r.currency})`] : [])],
      r.services.map((s) => [s.name, s.quantity, ...(r.includesMoney ? [s.amount] : [])]));
  }

  exportAging(a: ReceivablesReport): void {
    downloadCsv(`receivables-${a.asOf}.csv`, ['Patient', 'Open invoices', ...AGING_COLUMNS.map((c) => `${c.label} (${a.currency})`), `Total (${a.currency})`],
      a.patients.map((p) => [p.patientName, p.openInvoices, ...AGING_COLUMNS.map((c) => p.buckets[c.key]), p.buckets.total]));
  }

  private shortLabel(start: string): string {
    const g = this.report()?.granularity;
    return format(parseISO(start), g === 'Month' ? 'MMM yyyy' : 'MMM d');
  }

  private setRange(from: string, to: string): void {
    this.rangeError.set(null);
    this.range.set({ from, to });
  }

  private initialRange(): { from: string; to: string } {
    const from = this.params.get('from');
    const to = this.params.get('to');
    const valid = (v: string | null) => !!v && /^\d{4}-\d{2}-\d{2}$/.test(v);
    if (valid(from) && valid(to) && from! <= to! && differenceInCalendarDays(parseISO(to!), parseISO(from!)) + 1 <= MAX_DAYS) {
      return { from: from!, to: to! };
    }
    const today = this.today();
    return { from: format(startOfMonth(today), KEY), to: format(today, KEY) };
  }

  private matchPreset(range: { from: string; to: string }): Preset {
    const today = this.today();
    const is = (from: Date, to: Date) => range.from === format(from, KEY) && range.to === format(to, KEY);
    if (is(startOfMonth(today), today)) return 'this-month';
    if (is(startOfMonth(subMonths(today, 1)), endOfMonth(subMonths(today, 1)))) return 'last-month';
    if (is(addDays(today, -89), today)) return 'last-90';
    if (is(startOfYear(today), today)) return 'this-year';
    return 'custom';
  }

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: this.tab() === 'Receivables'
        ? { tab: 'Receivables', from: null, to: null }
        : { tab: null, from: this.range().from, to: this.range().to }
    });
  }
}

/** "↑ 12% vs before", or null when there's nothing to compare with. */
function change(current: number, previous: number): string | null {
  if (previous === 0) return current === 0 ? 'Same as before' : null;
  const pct = ((current - previous) / Math.abs(previous)) * 100;
  if (Math.abs(pct) < 0.5) return 'Same as before';
  return `${pct > 0 ? '↑' : '↓'} ${Math.abs(pct).toFixed(0)}% vs before`;
}

/** Change in a rate, in percentage points. */
function points(current: number | null, previous: number | null): string | null {
  if (current === null || previous === null) return null;
  const diff = (current - previous) * 100;
  if (Math.abs(diff) < 0.05) return 'Same as before';
  return `${diff > 0 ? '↑' : '↓'} ${Math.abs(diff).toFixed(1)} pts vs before`;
}
