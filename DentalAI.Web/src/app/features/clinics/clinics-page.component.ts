import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Clinic } from '../../core/models/clinic.models';
import { ClinicsService } from '../../core/services/clinics.service';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';

@Component({
  selector: 'app-clinics-page',
  standalone: true,
  imports: [NzCardModule, NzTableModule, PageHeaderComponent, PageStateComponent],
  template: `
    <section class="da-page">
      <app-page-header title="Clinics" eyebrow="Platform" subtitle="Every clinic (tenant) on DentalAI." />

      <nz-card>
        <app-page-state [status]="clinics.status()" [empty]="!clinics.value().length" (retry)="clinics.reload()"
                        emptyText="No clinics yet" errorText="We could not load clinics.">
          <nz-table [nzData]="clinics.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Name</th>
                <th>Slug</th>
                <th>Email</th>
                <th>Phone</th>
              </tr>
            </thead>
            <tbody>
              @for (clinic of clinics.value(); track clinic.id) {
                <tr>
                  <td><strong>{{ clinic.name }}</strong></td>
                  <td>{{ clinic.slug || '—' }}</td>
                  <td>{{ clinic.email || '—' }}</td>
                  <td>{{ clinic.phone || '—' }}</td>
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
export class ClinicsPageComponent {
  private readonly clinicsService = inject(ClinicsService);
  readonly clinics = remoteData<Clinic[]>(() => this.clinicsService.getAll(), []);
}
