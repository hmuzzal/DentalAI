import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { STAFF_ROLE_LABELS, STAFF_STATUS, StaffMember, StaffRole } from '../../core/models/staff.models';

/**
 * Staff list with actions. Clinic admins can change roles; platform admins (canChangeRoles = false) can't.
 * The signed-in user's own row has no role or deactivate controls (the API enforces the same rules).
 */
@Component({
  selector: 'app-staff-table',
  standalone: true,
  imports: [DatePipe, FormsModule, NzButtonModule, NzPopconfirmModule, NzSelectModule, NzTableModule, NzTagModule],
  template: `
    <nz-table [nzData]="members()" [nzPageSize]="25" [nzHideOnSinglePage]="true" nzSize="middle" [nzLoading]="loading()">
      <thead>
        <tr>
          <th>Name</th>
          <th>Role</th>
          <th>Status</th>
          <th nzAlign="right"><span class="sr-only">Actions</span></th>
        </tr>
      </thead>
      <tbody>
        @for (member of members(); track member.id) {
          <tr [class.is-off]="member.status === 'Deactivated'">
            <td>
              <strong>{{ member.fullName }}</strong>
              @if (member.id === currentUserId()) { <span class="you">(you)</span> }
              <div class="muted">{{ member.email }}</div>
            </td>
            <td>
              @if (canChangeRoles() && member.id !== currentUserId() && member.status !== 'Deactivated') {
                <nz-select class="role" nzSize="small" [ngModel]="member.role" [nzDisabled]="busyId() === member.id"
                           (ngModelChange)="roleChange.emit({ member, role: $event })"
                           [attr.aria-label]="'Role for ' + member.fullName">
                  @for (role of roles; track role) { <nz-option [nzValue]="role" [nzLabel]="roleLabels[role]" /> }
                </nz-select>
              } @else {
                {{ roleLabels[member.role] }}
              }
            </td>
            <td>
              <nz-tag [nzColor]="statuses[member.status].color">{{ statuses[member.status].label }}</nz-tag>
              @if (member.status === 'Invited' && member.inviteExpiresAtUtc) {
                <div class="muted">Link expires {{ member.inviteExpiresAtUtc | date: 'MMM d' }}</div>
              }
            </td>
            <td nzAlign="right" class="nowrap">
              @if (member.status === 'Invited' || member.status === 'InviteExpired') {
                <button nz-button nzType="link" nzSize="small" [nzLoading]="busyId() === member.id"
                        nz-popconfirm nzPopconfirmTitle="Create a new invite link? The current link will stop working."
                        nzOkText="Create link" (nzOnConfirm)="renew.emit(member)">New link</button>
              }
              @if (member.id !== currentUserId()) {
                @if (member.status === 'Deactivated') {
                  <button nz-button nzType="link" nzSize="small" [nzLoading]="busyId() === member.id"
                          (click)="setActive.emit({ member, active: true })">Reactivate</button>
                } @else {
                  <button nz-button nzType="link" nzSize="small" nzDanger [nzLoading]="busyId() === member.id"
                          nz-popconfirm [nzPopconfirmTitle]="'Deactivate ' + member.fullName + '? They will be signed out and can’t sign in until reactivated.'"
                          nzOkText="Deactivate" nzOkDanger (nzOnConfirm)="setActive.emit({ member, active: false })">Deactivate</button>
                }
              }
            </td>
          </tr>
        }
      </tbody>
    </nz-table>
  `,
  styles: [`
    .muted { color: var(--da-ink-2); font-size: 13px; }
    .you { color: var(--da-ink-3); margin-left: 4px; }
    .role { min-width: 140px; }
    .nowrap { white-space: nowrap; }
    .is-off td { opacity: .7; }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StaffTableComponent {
  readonly members = input<StaffMember[]>([]);
  readonly currentUserId = input<string | null>(null);
  readonly canChangeRoles = input(true);
  readonly busyId = input<string | null>(null);
  readonly loading = input(false);
  readonly roleChange = output<{ member: StaffMember; role: StaffRole }>();
  readonly setActive = output<{ member: StaffMember; active: boolean }>();
  readonly renew = output<StaffMember>();

  readonly roles: StaffRole[] = ['ClinicAdmin', 'Receptionist'];
  readonly roleLabels = STAFF_ROLE_LABELS;
  readonly statuses = STAFF_STATUS;
}
