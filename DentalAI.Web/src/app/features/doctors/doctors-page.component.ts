import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzTableModule } from 'ng-zorro-antd/table';
import { finalize } from 'rxjs';
import { Doctor } from '../../core/models/doctor.models';
import { ClinicOptionsService } from '../../core/services/clinic-options.service';
import { DoctorsService } from '../../core/services/doctors.service';
import { matchesSearch } from '../../shared/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { DoctorFormDrawerComponent } from './doctor-form-drawer.component';

@Component({
  selector: 'app-doctors-page',
  standalone: true,
  imports: [
    FormsModule, NzButtonModule, NzCardModule, NzIconModule, NzInputModule, NzPopconfirmModule, NzTableModule,
    PageHeaderComponent, PageStateComponent, DoctorFormDrawerComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Doctors" subtitle="Dentists and hygienists who see patients at this clinic.">
        <button nz-button nzType="primary" (click)="openForm(null)">
          <span nz-icon nzType="plus"></span> New doctor
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="doctors.status()" [empty]="!doctors.value().length" (retry)="doctors.reload()"
                        emptyText="No doctors yet" errorText="We could not load doctors.">
          <div class="toolbar">
            <nz-input-group class="search" [nzPrefix]="searchIcon">
              <input nz-input type="search" placeholder="Search name, specialty, email or phone"
                     aria-label="Search doctors" [ngModel]="search()" (ngModelChange)="search.set($event)" />
            </nz-input-group>
            <ng-template #searchIcon><span nz-icon nzType="search"></span></ng-template>
            <span class="count">{{ countText() }}</span>
          </div>

          <nz-table #table [nzData]="filtered()" [nzPageSize]="20" [nzHideOnSinglePage]="true" nzSize="middle"
                    [nzNoResult]="noMatches">
            <thead>
              <tr>
                <th>Name</th>
                <th>Specialty</th>
                @if (clinicOptions.isSuperAdmin()) { <th>Clinic</th> }
                <th>Phone</th>
                <th>Email</th>
                <th nzAlign="right"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (doctor of table.data; track doctor.id) {
                <tr>
                  <td><strong>{{ doctor.firstName }} {{ doctor.lastName }}</strong></td>
                  <td>{{ doctor.specialty || '—' }}</td>
                  @if (clinicOptions.isSuperAdmin()) { <td>{{ clinicOptions.clinicName(doctor.clinicId) }}</td> }
                  <td class="nowrap">{{ doctor.phone || '—' }}</td>
                  <td>{{ doctor.email || '—' }}</td>
                  <td nzAlign="right" class="nowrap">
                    <button nz-button nzType="link" (click)="openForm(doctor)">Edit</button>
                    <button nz-button nzType="link" nzDanger [nzLoading]="deletingId() === doctor.id"
                            nz-popconfirm [nzPopconfirmTitle]="'Delete ' + doctor.firstName + ' ' + doctor.lastName + '?'"
                            nzOkText="Delete" nzOkDanger (nzOnConfirm)="remove(doctor)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #noMatches>No doctors match “{{ search() }}”.</ng-template>
        </app-page-state>
      </nz-card>

      <app-doctor-form-drawer [open]="formOpen()" [doctor]="editing()"
                              (closed)="formOpen.set(false)" (saved)="onSaved()" />
    </section>
  `,
  styleUrl: '../../shared/list-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DoctorsPageComponent {
  private readonly doctorsService = inject(DoctorsService);
  private readonly messages = inject(NzMessageService);
  readonly clinicOptions = inject(ClinicOptionsService);

  readonly doctors = remoteData<Doctor[]>(() => this.doctorsService.getAll(), []);
  readonly search = signal('');
  readonly filtered = computed(() =>
    this.doctors.value().filter((d) =>
      matchesSearch(this.search(), d.firstName, d.lastName, d.specialty, d.email, d.phone)));
  readonly countText = computed(() => {
    const total = this.doctors.value().length;
    const shown = this.filtered().length;
    return shown === total ? `${total} doctor${total === 1 ? '' : 's'}` : `${shown} of ${total}`;
  });

  readonly formOpen = signal(false);
  readonly editing = signal<Doctor | null>(null);
  readonly deletingId = signal<string | null>(null);

  openForm(doctor: Doctor | null): void {
    this.editing.set(doctor);
    this.formOpen.set(true);
  }

  onSaved(): void {
    this.formOpen.set(false);
    this.doctors.reload();
  }

  remove(doctor: Doctor): void {
    this.deletingId.set(doctor.id);
    this.doctorsService.delete(doctor.id).pipe(finalize(() => this.deletingId.set(null))).subscribe({
      next: () => {
        this.messages.success(`${doctor.firstName} ${doctor.lastName} was deleted.`);
        this.doctors.reload();
      }
      // Errors (e.g. 409: upcoming appointments) are shown by the global error toast.
    });
  }
}
