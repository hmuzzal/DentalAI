import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Clinic } from '../../core/models/clinic.models';
import { ClinicsService } from '../../core/services/clinics.service';

@Component({
  selector: 'app-clinics-page',
  standalone: true,
  imports: [CommonModule, NzCardModule, NzTableModule],
  template: `
    <nz-card nzTitle="Clinics" class="table-card">
      <nz-table [nzData]="clinics()" [nzFrontPagination]="false">
        <thead>
          <tr>
            <th>Name</th>
            <th>Slug</th>
            <th>Email</th>
            <th>Phone</th>
          </tr>
        </thead>
        <tbody>
          @for (clinic of clinics(); track clinic.id) {
            <tr>
              <td>{{ clinic.name }}</td>
              <td>{{ clinic.slug }}</td>
              <td>{{ clinic.email || 'N/A' }}</td>
              <td>{{ clinic.phone || 'N/A' }}</td>
            </tr>
          }
        </tbody>
      </nz-table>
    </nz-card>
  `,
  styles: [`.table-card { border-radius: 1.25rem; }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClinicsPageComponent {
  private readonly clinicsService = inject(ClinicsService);
  readonly clinics = signal<Clinic[]>([]);

  constructor() {
    this.clinicsService.getAll().subscribe((data) => this.clinics.set(data));
  }
}
