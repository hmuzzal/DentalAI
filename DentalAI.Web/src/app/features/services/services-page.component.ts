import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { DentalService } from '../../core/models/service.models';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-services-page',
  standalone: true,
  imports: [CurrencyPipe, NzCardModule, NzTableModule, NzTagModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Services"
                       subtitle="Treatments patients can book. Duration sets the length of each appointment." />

      <nz-card>
        <app-page-state [status]="services.status()" [empty]="!services.value().length" (retry)="services.reload()"
                        emptyText="No services yet" errorText="We could not load services.">
          <nz-table [nzData]="services.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Name</th>
                <th nzAlign="right">Duration</th>
                <th nzAlign="right">Price</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (service of services.value(); track service.id) {
                <tr>
                  <td><strong>{{ service.name }}</strong></td>
                  <td nzAlign="right" class="num">{{ service.durationMinutes }} min</td>
                  <td nzAlign="right" class="num">{{ service.price | currency: 'USD' }}</td>
                  <td>
                    <nz-tag [nzColor]="service.isActive ? 'green' : 'default'">
                      {{ service.isActive ? 'Active' : 'Inactive' }}
                    </nz-tag>
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
        </app-page-state>
      </nz-card>
    </section>
  `,
  styles: [`.num { font-variant-numeric: tabular-nums; }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ServicesPageComponent {
  private readonly servicesCatalogService = inject(ServicesCatalogService);
  readonly services = remoteData<DentalService[]>(() => this.servicesCatalogService.getAll(), []);
}
