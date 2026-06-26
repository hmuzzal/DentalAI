import { CommonModule, CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { DentalService } from '../../core/models/service.models';
import { ServicesCatalogService } from '../../core/services/services-catalog.service';

@Component({
  selector: 'app-services-page',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, NzCardModule, NzTableModule, NzTagModule],
  template: `
    <nz-card nzTitle="Services" class="table-card">
      <nz-table [nzData]="services()" [nzFrontPagination]="false">
        <thead>
          <tr>
            <th>Name</th>
            <th>Duration</th>
            <th>Price</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          @for (service of services(); track service.id) {
            <tr>
              <td>{{ service.name }}</td>
              <td>{{ service.durationMinutes }} min</td>
              <td>{{ service.price | currency }}</td>
              <td>
                <nz-tag [nzColor]="service.isActive ? 'green' : 'default'">
                  {{ service.isActive ? 'Active' : 'Inactive' }}
                </nz-tag>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </nz-card>
  `,
  styles: [`.table-card { border-radius: 1.25rem; }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ServicesPageComponent {
  private readonly servicesCatalogService = inject(ServicesCatalogService);
  readonly services = signal<DentalService[]>([]);

  constructor() {
    this.servicesCatalogService.getAll().subscribe((data) => this.services.set(data));
  }
}
