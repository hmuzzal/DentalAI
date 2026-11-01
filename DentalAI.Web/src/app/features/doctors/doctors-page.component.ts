import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Doctor } from '../../core/models/doctor.models';
import { DoctorsService } from '../../core/services/doctors.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-doctors-page',
  standalone: true,
  imports: [NzCardModule, NzTableModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Doctors" subtitle="Dentists and hygienists who see patients at this clinic." />

      <nz-card>
        <app-page-state [status]="doctors.status()" [empty]="!doctors.value().length" (retry)="doctors.reload()"
                        emptyText="No doctors yet" errorText="We could not load doctors.">
          <nz-table [nzData]="doctors.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Name</th>
                <th>Specialty</th>
                <th>Phone</th>
                <th>Email</th>
              </tr>
            </thead>
            <tbody>
              @for (doctor of doctors.value(); track doctor.id) {
                <tr>
                  <td><strong>{{ doctor.firstName }} {{ doctor.lastName }}</strong></td>
                  <td>{{ doctor.specialty || '—' }}</td>
                  <td>{{ doctor.phone || '—' }}</td>
                  <td>{{ doctor.email || '—' }}</td>
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
export class DoctorsPageComponent {
  private readonly doctorsService = inject(DoctorsService);
  readonly doctors = remoteData<Doctor[]>(() => this.doctorsService.getAll(), []);
}
