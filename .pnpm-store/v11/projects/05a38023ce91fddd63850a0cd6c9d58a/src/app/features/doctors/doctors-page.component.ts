import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Doctor } from '../../core/models/doctor.models';
import { DoctorsService } from '../../core/services/doctors.service';

@Component({
  selector: 'app-doctors-page',
  standalone: true,
  imports: [CommonModule, NzCardModule, NzTableModule, NzTagModule],
  template: `
    <nz-card nzTitle="Doctors" class="table-card">
      <nz-table [nzData]="doctors()" [nzFrontPagination]="false">
        <thead>
          <tr>
            <th>Name</th>
            <th>Specialization</th>
            <th>Hours</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          @for (doctor of doctors(); track doctor.id) {
            <tr>
              <td>{{ doctor.fullName }}</td>
              <td>{{ doctor.specialization }}</td>
              <td>{{ doctor.workDayStart }} - {{ doctor.workDayEnd }}</td>
              <td>
                <nz-tag [nzColor]="doctor.isActive ? 'green' : 'default'">
                  {{ doctor.isActive ? 'Active' : 'Inactive' }}
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
export class DoctorsPageComponent {
  private readonly doctorsService = inject(DoctorsService);
  readonly doctors = signal<Doctor[]>([]);

  constructor() {
    this.doctorsService.getAll().subscribe((data) => this.doctors.set(data));
  }
}
