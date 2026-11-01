import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTableModule } from 'ng-zorro-antd/table';
import { Clinic } from '../../core/models/clinic.models';
import { ClinicsService } from '../../core/services/clinics.service';
import { timeZoneLabel } from '../../core/time-zone';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { ClinicFormDrawerComponent } from './clinic-form-drawer.component';

@Component({
  selector: 'app-clinics-page',
  standalone: true,
  imports: [
    NzButtonModule, NzCardModule, NzIconModule, NzTableModule, PageHeaderComponent, PageStateComponent,
    ClinicFormDrawerComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Clinics" eyebrow="Platform" subtitle="Every clinic (tenant) on DentalAI.">
        <button nz-button nzType="primary" (click)="openForm(null)">
          <span nz-icon nzType="plus"></span> New clinic
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="clinics.status()" [empty]="!clinics.value().length" (retry)="clinics.reload()"
                        emptyText="No clinics yet" errorText="We could not load clinics.">
          <nz-table [nzData]="clinics.value()" [nzFrontPagination]="false" nzSize="middle">
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Phone</th>
                <th>Time zone</th>
                <th>Slots</th>
                <th nzWidth="80px"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (clinic of clinics.value(); track clinic.id) {
                <tr>
                  <td><strong>{{ clinic.name }}</strong></td>
                  <td>{{ clinic.email || '—' }}</td>
                  <td>{{ clinic.phone || '—' }}</td>
                  <td>
                    {{ zoneName(clinic.timeZone) ?? "—" }}
                    @if (zoneName(clinic.timeZone) !== clinic.timeZone) { <div class="muted">{{ clinic.timeZone }}</div> }
                  </td>
                  <td>Every {{ clinic.slotIntervalMinutes }} min</td>
                  <td><button nz-button nzType="link" (click)="openForm(clinic)">Edit</button></td>
                </tr>
              }
            </tbody>
          </nz-table>
        </app-page-state>
      </nz-card>

      <app-clinic-form-drawer [open]="formOpen()" [clinic]="editing()"
                              (closed)="formOpen.set(false)" (saved)="onSaved()" />
    </section>
  `,
  styles: [`
    .muted { color: var(--da-ink-2); }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClinicsPageComponent {
  private readonly clinicsService = inject(ClinicsService);
  readonly clinics = remoteData<Clinic[]>(() => this.clinicsService.getAll(), []);

  readonly formOpen = signal(false);
  readonly editing = signal<Clinic | null>(null);

  openForm(clinic: Clinic | null): void {
    this.editing.set(clinic);
    this.formOpen.set(true);
  }

  onSaved(): void {
    this.formOpen.set(false);
    this.clinics.reload();
  }

  zoneName(timeZone: string | null | undefined): string | null {
    return timeZoneLabel(timeZone);
  }
}
