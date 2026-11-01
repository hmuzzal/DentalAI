import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzTableModule } from 'ng-zorro-antd/table';
import { catchError, debounceTime, distinctUntilChanged, finalize, map, of, Subject, switchMap, tap } from 'rxjs';
import { PagedResult } from '../../core/models/api-response.model';
import { Patient } from '../../core/models/patient.models';
import { PatientsService } from '../../core/services/patients.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { DateOnlyPipe } from '../../shared/date-only.pipe';
import { PhonePipe } from '../../shared/phone.pipe';
import { PageStateComponent } from '../../shared/page-state.component';
import { LoadStatus } from '../../shared/remote-data';
import { PatientFormDrawerComponent } from './patient-form-drawer.component';

interface Query {
  search: string;
  page: number;
  pageSize: number;
  /** Bumped to re-run the same query (after a save or delete). */
  version: number;
}

/** Patients can number in the thousands, so search and paging happen on the server. */
@Component({
  selector: 'app-patients-page',
  standalone: true,
  imports: [
    DateOnlyPipe, FormsModule, RouterLink, NzButtonModule, NzCardModule, NzIconModule, NzInputModule, NzPopconfirmModule,
    NzTableModule, PageHeaderComponent, PageStateComponent, PhonePipe, PatientFormDrawerComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Patients" subtitle="Everyone registered with this clinic.">
        <button nz-button nzType="primary" (click)="openForm(null)">
          <span nz-icon nzType="plus"></span> New patient
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="status()" [empty]="noPatientsAtAll()" (retry)="reload()"
                        emptyText="No patients yet" errorText="We could not load patients.">
          <div class="toolbar">
            <nz-input-group class="search" [nzPrefix]="searchIcon">
              <input nz-input type="search" placeholder="Search name, phone or email" aria-label="Search patients"
                     [ngModel]="searchText()" (ngModelChange)="onSearch($event)" />
            </nz-input-group>
            <ng-template #searchIcon><span nz-icon nzType="search"></span></ng-template>
            <span class="count">{{ result().totalCount }} patient{{ result().totalCount === 1 ? '' : 's' }}</span>
          </div>

          <nz-table [nzData]="result().items" [nzFrontPagination]="false" [nzLoading]="fetching()"
                    [nzTotal]="result().totalCount" [nzPageIndex]="query().page" [nzPageSize]="query().pageSize"
                    [nzShowSizeChanger]="true" [nzPageSizeOptions]="[10, 20, 50, 100]" [nzHideOnSinglePage]="false"
                    (nzPageIndexChange)="setPage($event)" (nzPageSizeChange)="setPageSize($event)"
                    nzSize="middle" [nzNoResult]="noMatches">
            <thead>
              <tr>
                <th>Name</th>
                <th>Phone</th>
                <th>Email</th>
                <th>Date of birth</th>
                <th nzAlign="right"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (patient of result().items; track patient.id) {
                <tr>
                  <td>
                    <strong>{{ patient.fullName }}</strong>
                    @if (patient.gender) { <div class="muted">{{ patient.gender }}</div> }
                  </td>
                  <td class="nowrap">{{ (patient.phone | phone) || '—' }}</td>
                  <td>{{ patient.email || '—' }}</td>
                  <td class="nowrap num">{{ patient.dateOfBirth ? (patient.dateOfBirth | dateOnly: 'MM/dd/yyyy') : '—' }}</td>
                  <td nzAlign="right" class="nowrap">
                    <a nz-button nzType="link" routerLink="/invoices" [queryParams]="{ patient: patient.id }">Invoices</a>
                    <button nz-button nzType="link" (click)="openForm(patient)">Edit</button>
                    <button nz-button nzType="link" nzDanger [nzLoading]="deletingId() === patient.id"
                            nz-popconfirm [nzPopconfirmTitle]="'Delete ' + patient.fullName + '? Their past appointments are kept.'"
                            nzOkText="Delete" nzOkDanger (nzOnConfirm)="remove(patient)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #noMatches>No patients match “{{ query().search }}”.</ng-template>
        </app-page-state>
      </nz-card>

      <app-patient-form-drawer [open]="formOpen()" [patient]="editing()"
                               (closed)="formOpen.set(false)" (saved)="onSaved()" />
    </section>
  `,
  styleUrl: '../../shared/list-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientsPageComponent {
  private readonly patientsService = inject(PatientsService);
  private readonly messages = inject(NzMessageService);

  readonly query = signal<Query>({ search: '', page: 1, pageSize: 20, version: 0 });
  readonly result = signal<PagedResult<Patient>>({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  /** Page-level state: skeleton on first load, error with retry. Later fetches only dim the table. */
  readonly status = signal<LoadStatus>('loading');
  readonly fetching = signal(false);
  readonly searchText = signal('');
  readonly noPatientsAtAll = computed(() => this.result().totalCount === 0 && !this.query().search);

  readonly formOpen = signal(false);
  readonly editing = signal<Patient | null>(null);
  readonly deletingId = signal<string | null>(null);

  private readonly searchInput = new Subject<string>();

  constructor() {
    const destroyRef = inject(DestroyRef);

    this.searchInput
      .pipe(map((text) => text.trim()), debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(destroyRef))
      .subscribe((search) => this.query.update((q) => ({ ...q, search, page: 1 })));

    toObservable(this.query)
      .pipe(
        tap(() => this.fetching.set(true)),
        switchMap((q) =>
          this.patientsService.search(q).pipe(
            map((page) => ({ ok: true as const, page })),
            catchError(() => of({ ok: false as const })),
            finalize(() => this.fetching.set(false))
          )
        ),
        takeUntilDestroyed(destroyRef)
      )
      .subscribe((outcome) => {
        this.fetching.set(false);
        if (!outcome.ok) {
          if (this.status() === 'loading') this.status.set('error');
          return;
        }
        const page = outcome.page;
        // A delete can empty the last page: step back one.
        if (!page.items.length && page.page > 1 && page.totalCount > 0) {
          this.setPage(Math.ceil(page.totalCount / page.pageSize));
          return;
        }
        this.result.set(page);
        this.status.set('ready');
      });
  }

  onSearch(text: string): void {
    this.searchText.set(text);
    this.searchInput.next(text);
  }

  setPage(page: number): void {
    this.query.update((q) => (q.page === page ? q : { ...q, page }));
  }

  setPageSize(pageSize: number): void {
    this.query.update((q) => ({ ...q, pageSize, page: 1 }));
  }

  reload(): void {
    this.status.set('loading');
    this.query.update((q) => ({ ...q, version: q.version + 1 }));
  }

  openForm(patient: Patient | null): void {
    this.editing.set(patient);
    this.formOpen.set(true);
  }

  onSaved(): void {
    this.formOpen.set(false);
    this.query.update((q) => ({ ...q, version: q.version + 1 }));
  }

  remove(patient: Patient): void {
    this.deletingId.set(patient.id);
    this.patientsService.delete(patient.id).pipe(finalize(() => this.deletingId.set(null))).subscribe({
      next: () => {
        this.messages.success(`${patient.fullName} was deleted.`);
        this.query.update((q) => ({ ...q, version: q.version + 1 }));
      }
    });
  }
}
