import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Patient } from '../../core/models/patient.models';
import { PatientsService } from '../../core/services/patients.service';

@Component({
  selector: 'app-patients-page',
  standalone: true,
  imports: [CommonModule, NzCardModule, NzTableModule],
  template: `
    <nz-card nzTitle="Patients" class="table-card">
      <nz-table [nzData]="patients()" [nzFrontPagination]="false">
        <thead>
          <tr>
            <th>Name</th>
            <th>Phone</th>
            <th>Email</th>
            <th>Gender</th>
          </tr>
        </thead>
        <tbody>
          @for (patient of patients(); track patient.id) {
            <tr>
              <td>{{ patient.fullName }}</td>
              <td>{{ patient.phone }}</td>
              <td>{{ patient.email || 'N/A' }}</td>
              <td>{{ patient.gender || 'N/A' }}</td>
            </tr>
          }
        </tbody>
      </nz-table>
    </nz-card>
  `,
  styles: [`.table-card { border-radius: 1.25rem; }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientsPageComponent {
  private readonly patientsService = inject(PatientsService);
  readonly patients = signal<Patient[]>([]);

  constructor() {
    this.patientsService.getAll().subscribe((data) => this.patients.set(data));
  }
}
