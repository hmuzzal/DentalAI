import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { catchError, finalize, map, of, switchMap, tap } from 'rxjs';
import { PagedResult } from '../../core/models/api-response.model';
import { MESSAGE_KIND_LABELS, MESSAGE_STATUS, MessageStatus, OutboundMessage } from '../../core/models/notification.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { NotificationsService } from '../../core/services/notifications.service';
import { effectiveTimeZone, formatInZone } from '../../core/time-zone';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { PhonePipe } from '../../shared/phone.pipe';
import { LoadStatus } from '../../shared/remote-data';

type Filter = 'All' | 'Scheduled' | 'Sent' | 'Failed';
const FILTERS: Filter[] = ['All', 'Scheduled', 'Sent', 'Failed'];
const FILTER_STATUS: Record<Filter, MessageStatus | null> = { All: null, Scheduled: 'Pending', Sent: 'Sent', Failed: 'Failed' };
const EMPTY: PagedResult<OutboundMessage> = { items: [], totalCount: 0, page: 1, pageSize: 25 };

interface Query {
  filter: Filter;
  patientId: string | null;
  appointmentId: string | null;
  page: number;
  version: number;
}

/** /messages: every email and text the clinic has sent or will send. ?patient=<id> or ?appointment=<id> narrows it. */
@Component({
  selector: 'app-messages-page',
  standalone: true,
  imports: [
    FormsModule, RouterLink, NzButtonModule, NzCardModule, NzIconModule, NzSegmentedModule, NzTableModule, NzTagModule,
    PageHeaderComponent, PageStateComponent, PhonePipe
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Messages" subtitle="Confirmations, reminders and other messages to patients and staff.">
        <button nz-button (click)="reload()" aria-label="Refresh"><span nz-icon nzType="reload"></span> Refresh</button>
        @if (isAdmin()) { <a nz-button routerLink="/settings/notifications"><span nz-icon nzType="setting"></span> Settings</a> }
      </app-page-header>

      <nz-card>
        <app-page-state [status]="status()" (retry)="reload()" [rows]="6" errorText="We could not load messages.">
          <div class="toolbar">
            <nz-segmented [nzOptions]="filters" [ngModel]="query().filter" (ngModelChange)="setFilter($event)" aria-label="Show" />
            @if (query().patientId) {
              <nz-tag nzMode="closeable" (nzOnClose)="clearScope()">Patient: {{ result().items[0]?.patientName || '…' }}</nz-tag>
            }
            @if (query().appointmentId) {
              <nz-tag nzMode="closeable" (nzOnClose)="clearScope()">One appointment</nz-tag>
            }
            <span class="count">{{ result().totalCount }} message{{ result().totalCount === 1 ? '' : 's' }}</span>
          </div>

          <nz-table [nzData]="result().items" [nzFrontPagination]="false" [nzLoading]="fetching()" [nzTotal]="result().totalCount"
                    [nzPageIndex]="query().page" [nzPageSize]="pageSize" (nzPageIndexChange)="setPage($event)"
                    [nzHideOnSinglePage]="true" nzSize="middle" [nzNoResult]="none">
            <thead>
              <tr>
                <th nzWidth="40px"></th>
                <th>When</th>
                <th>To</th>
                <th>Message</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (m of result().items; track m.id) {
                <tr>
                  <td [nzExpand]="expanded().has(m.id)" (nzExpandChange)="toggle(m.id)" [attr.aria-label]="'Show the message to ' + (m.patientName ?? m.to)"></td>
                  <td class="num nowrap">
                    {{ when(m) }}
                    @if (m.status === 'Pending' && isLater(m)) { <br /><small class="muted">scheduled</small> }
                  </td>
                  <td>
                    @if (m.patientName) { <strong>{{ m.patientName }}</strong><br /> }
                    <small class="muted">
                      <span nz-icon [nzType]="m.channel === 'Sms' ? 'message' : 'mail'"></span>
                      {{ m.channel === 'Sms' ? (m.to | phone) : m.to }}
                    </small>
                  </td>
                  <td>
                    {{ kinds[m.kind] }}@if (m.reminderHours) { <span class="muted"> · {{ m.reminderHours }} h before</span> }
                    @if (m.subject) { <br /><small class="muted">{{ m.subject }}</small> }
                  </td>
                  <td>
                    <nz-tag [nzColor]="statuses[m.status].color">{{ statuses[m.status].label }}</nz-tag>
                    @if (m.lastError && m.status !== 'Sent') { <br /><small class="muted">{{ m.lastError }}</small> }
                  </td>
                </tr>
                <tr [nzExpand]="expanded().has(m.id)">
                  <pre class="body">{{ m.body }}</pre>
                  @if (m.attempts > 1) { <p class="muted">{{ m.attempts }} attempts.</p> }
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #none>
            <div class="none">
              <p>{{ query().filter !== 'All' || query().patientId || query().appointmentId ? 'No messages match.' : 'No messages yet.' }}</p>
              <p class="muted">Booking an appointment sends the patient a confirmation; reminders follow before the visit.</p>
            </div>
          </ng-template>
        </app-page-state>
      </nz-card>
    </section>
  `,
  styles: [`
    .toolbar { display: flex; align-items: center; gap: var(--da-space-3); flex-wrap: wrap; margin-bottom: var(--da-space-3); }
    .count { margin-left: auto; color: var(--da-ink-2); }
    .num { font-variant-numeric: tabular-nums; }
    .nowrap { white-space: nowrap; }
    .muted { color: var(--da-ink-2); }
    .body { white-space: pre-wrap; font-family: inherit; margin: 0; max-width: 72ch; }
    .none { text-align: center; padding: var(--da-space-4) 0; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MessagesPageComponent {
  private readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly auth = inject(AuthStateService);
  private readonly timeZone = computed(() => effectiveTimeZone(this.auth.user()?.clinicTimeZone));
  readonly isAdmin = computed(() => this.auth.user()?.role === 'ClinicAdmin');
  readonly statuses = MESSAGE_STATUS;
  readonly kinds = MESSAGE_KIND_LABELS;
  readonly filters = FILTERS;
  readonly pageSize = 25;
  private readonly params = this.route.snapshot.queryParamMap;
  readonly query = signal<Query>({
    filter: FILTERS.includes(this.params.get('filter') as Filter) ? (this.params.get('filter') as Filter) : 'All',
    patientId: this.params.get('patient'),
    appointmentId: this.params.get('appointment'),
    page: Number(this.params.get('page')) || 1,
    version: 0
  });
  readonly result = signal(EMPTY);
  readonly status = signal<LoadStatus>('loading');
  readonly fetching = signal(false);
  readonly expanded = signal(new Set<string>());

  constructor() {
    toObservable(this.query).pipe(
      tap((q) => this.syncUrl(q)),
      tap(() => this.fetching.set(true)),
      switchMap((q) => this.notifications.messages({
        status: FILTER_STATUS[q.filter], patientId: q.patientId, appointmentId: q.appointmentId, page: q.page, pageSize: this.pageSize
      }).pipe(
        map((page) => ({ ok: true as const, page })),
        catchError(() => of({ ok: false as const })),
        finalize(() => this.fetching.set(false))
      )),
      takeUntilDestroyed(inject(DestroyRef))
    ).subscribe((outcome) => {
      if (!outcome.ok) {
        if (this.status() === 'loading') this.status.set('error');
        return;
      }
      this.result.set(outcome.page);
      this.status.set('ready');
    });
  }

  /** Sent time, or when it is due; in the clinic's time zone like the rest of the app. */
  when(m: OutboundMessage): string {
    return formatInZone(m.sentAtUtc ?? m.notBeforeUtc, this.timeZone(), { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  }

  isLater(m: OutboundMessage): boolean {
    return new Date(m.notBeforeUtc).getTime() > Date.now();
  }

  toggle(id: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  setFilter(filter: Filter): void {
    this.query.update((q) => ({ ...q, filter, page: 1 }));
  }

  setPage(page: number): void {
    this.query.update((q) => (q.page === page ? q : { ...q, page }));
  }

  clearScope(): void {
    this.query.update((q) => ({ ...q, patientId: null, appointmentId: null, page: 1 }));
  }

  reload(): void {
    this.query.update((q) => ({ ...q, version: q.version + 1 }));
  }

  private syncUrl(q: Query): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        filter: q.filter === 'All' ? null : q.filter,
        patient: q.patientId,
        appointment: q.appointmentId,
        page: q.page > 1 ? q.page : null
      }
    });
  }
}
