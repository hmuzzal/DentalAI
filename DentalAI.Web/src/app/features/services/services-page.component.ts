import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzSegmentedModule } from 'ng-zorro-antd/segmented';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { finalize } from 'rxjs';
import { DentalService } from '../../core/models/service.models';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';
import { matchesSearch } from '../../shared/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { ServiceFormDrawerComponent } from './service-form-drawer.component';

type StatusFilter = 'All' | 'Active' | 'Inactive';

@Component({
  selector: 'app-services-page',
  standalone: true,
  imports: [
    CurrencyPipe, FormsModule, NzButtonModule, NzCardModule, NzIconModule, NzInputModule, NzPopconfirmModule,
    NzSegmentedModule, NzTableModule, NzTagModule, PageHeaderComponent, PageStateComponent, ServiceFormDrawerComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Services"
                       subtitle="Treatments patients can book. Duration sets the length of each appointment.">
        <button nz-button nzType="primary" (click)="openForm(null)">
          <span nz-icon nzType="plus"></span> New service
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="services.status()" [empty]="!services.value().length" (retry)="services.reload()"
                        emptyText="No services yet" errorText="We could not load services.">
          <div class="toolbar">
            <nz-input-group class="search" [nzPrefix]="searchIcon">
              <input nz-input type="search" placeholder="Search services" aria-label="Search services"
                     [ngModel]="search()" (ngModelChange)="search.set($event)" />
            </nz-input-group>
            <ng-template #searchIcon><span nz-icon nzType="search"></span></ng-template>
            <nz-segmented [nzOptions]="statusOptions" [ngModel]="status()" (ngModelChange)="status.set($event)"
                          aria-label="Filter by status" />
            <span class="count">{{ countText() }}</span>
          </div>

          <nz-table #table [nzData]="filtered()" [nzPageSize]="20" [nzHideOnSinglePage]="true" nzSize="middle"
                    [nzNoResult]="noMatches">
            <thead>
              <tr>
                <th>Name</th>
                <th nzAlign="right">Duration</th>
                <th nzAlign="right">Price</th>
                <th>Status</th>
                <th nzAlign="right"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (service of table.data; track service.id) {
                <tr>
                  <td>
                    <strong>{{ service.name }}</strong>
                    @if (service.description) { <div class="muted">{{ service.description }}</div> }
                  </td>
                  <td nzAlign="right" class="num nowrap">{{ service.durationMinutes }} min</td>
                  <td nzAlign="right" class="num">{{ service.price | currency: 'USD' }}</td>
                  <td>
                    <nz-tag [nzColor]="service.isActive ? 'green' : 'default'">
                      {{ service.isActive ? 'Active' : 'Inactive' }}
                    </nz-tag>
                  </td>
                  <td nzAlign="right" class="nowrap">
                    <button nz-button nzType="link" (click)="openForm(service)">Edit</button>
                    <button nz-button nzType="link" nzDanger [nzLoading]="deletingId() === service.id"
                            nz-popconfirm [nzPopconfirmTitle]="'Delete ' + service.name + '? Past appointments keep it; to stop new bookings only, mark it inactive instead.'"
                            nzOkText="Delete" nzOkDanger (nzOnConfirm)="remove(service)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #noMatches>No services match these filters.</ng-template>
        </app-page-state>
      </nz-card>

      <app-service-form-drawer [open]="formOpen()" [service]="editing()"
                               (closed)="formOpen.set(false)" (saved)="onSaved()" />
    </section>
  `,
  styleUrl: '../../shared/list-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ServicesPageComponent {
  private readonly servicesCatalog = inject(ServicesCatalogService);
  private readonly messages = inject(NzMessageService);

  readonly services = remoteData<DentalService[]>(() => this.servicesCatalog.getAll(), []);
  readonly statusOptions: StatusFilter[] = ['All', 'Active', 'Inactive'];
  readonly search = signal('');
  readonly status = signal<StatusFilter>('All');
  readonly filtered = computed(() => {
    const status = this.status();
    return this.services.value().filter((s) =>
      (status === 'All' || s.isActive === (status === 'Active'))
      && matchesSearch(this.search(), s.name, s.description));
  });
  readonly countText = computed(() => {
    const total = this.services.value().length;
    const shown = this.filtered().length;
    return shown === total ? `${total} service${total === 1 ? '' : 's'}` : `${shown} of ${total}`;
  });

  readonly formOpen = signal(false);
  readonly editing = signal<DentalService | null>(null);
  readonly deletingId = signal<string | null>(null);

  openForm(service: DentalService | null): void {
    this.editing.set(service);
    this.formOpen.set(true);
  }

  onSaved(): void {
    this.formOpen.set(false);
    this.services.reload();
  }

  remove(service: DentalService): void {
    this.deletingId.set(service.id);
    this.servicesCatalog.delete(service.id).pipe(finalize(() => this.deletingId.set(null))).subscribe({
      next: () => {
        this.messages.success(`${service.name} was deleted.`);
        this.services.reload();
      }
    });
  }
}
