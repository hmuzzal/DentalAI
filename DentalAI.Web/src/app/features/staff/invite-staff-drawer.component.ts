import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { finalize } from 'rxjs';
import { STAFF_ROLE_LABELS, StaffInvite, StaffRole } from '../../core/models/staff.models';
import { StaffService } from '../../core/services/staff.service';
import { FormDrawerComponent } from '../../shared/form-drawer.component';
import { saveErrorText, touchAll } from '../../shared/forms';

/**
 * Invite someone: name, email and role. Creates the account as "Invited" and emits the one-time link.
 * Pass clinicId when a platform admin invites for a clinic (they can only invite admins: roles = ['ClinicAdmin']).
 */
@Component({
  selector: 'app-invite-staff-drawer',
  standalone: true,
  imports: [ReactiveFormsModule, NzFormModule, NzInputModule, NzRadioModule, FormDrawerComponent],
  template: `
    <app-form-drawer [open]="open()" [title]="title()" submitLabel="Create invite link" [saving]="saving()"
                     [error]="error()" (closed)="closed.emit()" (submitted)="save()">
      <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
        <div class="row">
          <nz-form-item>
            <nz-form-label nzRequired nzFor="invite-first">First name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a first name.">
              <input nz-input id="invite-first" formControlName="firstName" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired nzFor="invite-last">Last name</nz-form-label>
            <nz-form-control nzErrorTip="Enter a last name.">
              <input nz-input id="invite-last" formControlName="lastName" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
        </div>
        <nz-form-item>
          <nz-form-label nzRequired nzFor="invite-email">Work email</nz-form-label>
          <nz-form-control nzErrorTip="Enter a valid email address." nzExtra="They sign in with this email.">
            <input nz-input id="invite-email" type="email" formControlName="email" autocomplete="off" />
          </nz-form-control>
        </nz-form-item>
        @if (roles().length > 1) {
          <nz-form-item>
            <nz-form-label nzRequired>Role</nz-form-label>
            <nz-form-control>
              <nz-radio-group formControlName="role">
                @for (role of roles(); track role) {
                  <label nz-radio [nzValue]="role">{{ roleLabels[role] }}</label>
                }
              </nz-radio-group>
              <p class="hint">
                @if (form.controls.role.value === 'ClinicAdmin') {
                  Admins manage doctors, services, schedules and staff, as well as everything receptionists do.
                } @else {
                  Receptionists book and manage appointments and patient records.
                }
              </p>
            </nz-form-control>
          </nz-form-item>
        }
        <p class="hint">
          You'll get a link to send them. They open it to set their own password; it works once and expires in 7 days.
        </p>
        <button type="submit" hidden></button>
      </form>
    </app-form-drawer>
  `,
  styles: [`
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .hint { color: var(--da-ink-2); font-size: 13px; margin: 4px 0 0; }
    @media (max-width: 480px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InviteStaffDrawerComponent {
  readonly open = input(false);
  /** Set for a platform admin inviting into a specific clinic. */
  readonly clinicId = input<string | null>(null);
  readonly roles = input<StaffRole[]>(['Receptionist', 'ClinicAdmin']);
  readonly title = input('Invite staff');
  readonly closed = output<void>();
  readonly invited = output<StaffInvite>();

  private readonly staff = inject(StaffService);
  readonly roleLabels = STAFF_ROLE_LABELS;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
    role: ['Receptionist' as StaffRole]
  });

  constructor() {
    effect(() => {
      if (!this.open()) return;
      const roles = this.roles();
      untracked(() => {
        this.error.set(null);
        this.form.reset({ firstName: '', lastName: '', email: '', role: roles[0] });
      });
    });
  }

  save(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.staff
      .invite({ firstName: v.firstName.trim(), lastName: v.lastName.trim(), email: v.email.trim(), role: v.role },
        this.clinicId() ?? undefined)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (invite) => this.invited.emit(invite),
        error: (error: unknown) => this.error.set(saveErrorText(error))
      });
  }
}
