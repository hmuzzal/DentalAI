import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Patient } from '../../core/models/patient.models';
import { PatientsService } from '../../core/services/patients.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-patients-page',
  standalone: true,
  imports: [NzCardModule, NzTableModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Patients" subtitle="Everyone registered with this clinic." />

      <nz-card>
        <app-page-state [status]="patients.status()" [empty]="!patients.value().length" (retry)="patients.reload()"
                        emptyText="No patients yet" errorText="We could not load patients.">
          <nz-table [nzData]="patients.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Name</th>
                <th>Phone</th>
                <th>Email</th>
                <th>Gender</th>
              </tr>
            </thead>
            <tbody>
              @for (patient of patients.value(); track patient.id) {
                <tr>
                  <td><strong>{{ patient.fullName }}</strong></td>
                  <td>{{ patient.phone || '—' }}</td>
                  <td>{{ patient.email || '—' }}</td>
                  <td>{{ patient.gender || '—' }}</td>
                </tr>
              }
            </tbody>
          </nz-table>
        </app-page-state>
      </nz-card>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientsPageComponent {
  private readonly patientsService = inject(PatientsService);
  readonly patients = remoteData<Patient[]>(() => this.patientsService.getAll(), []);
}
