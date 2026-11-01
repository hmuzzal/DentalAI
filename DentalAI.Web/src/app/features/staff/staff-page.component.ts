import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { finalize, Observable } from 'rxjs';
import { StaffInvite, StaffMember, StaffRole } from '../../core/models/staff.models';
import { AuthStateService } from '../../core/services/auth-state.service';
import { StaffService } from '../../core/services/staff.service';
import { InviteLinkModalComponent } from '../../shared/invite-link-modal.component';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { PageStateComponent } from '../../shared/page-state.component';
import { remoteData } from '../../shared/remote-data';
import { InviteStaffDrawerComponent } from './invite-staff-drawer.component';
import { StaffTableComponent } from './staff-table.component';

/** /staff (clinic admins): who can sign in to this clinic, and their roles. */
@Component({
  selector: 'app-staff-page',
  standalone: true,
  imports: [
    NzButtonModule, NzCardModule, NzIconModule, PageHeaderComponent, PageStateComponent, StaffTableComponent,
    InviteStaffDrawerComponent, InviteLinkModalComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Staff" subtitle="Everyone who can sign in to this clinic. Admins manage the clinic; receptionists run the front desk.">
        <button nz-button nzType="primary" (click)="inviteOpen.set(true)">
          <span nz-icon nzType="plus"></span> Invite staff
        </button>
      </app-page-header>

      <nz-card>
        <app-page-state [status]="staff.status()" [empty]="!staff.value().length" (retry)="staff.reload()"
                        emptyText="No staff yet" errorText="We could not load your staff.">
          <app-staff-table [members]="staff.value()" [currentUserId]="currentUserId()" [busyId]="busyId()"
                           (roleChange)="changeRole($event.member, $event.role)"
                           (setActive)="setActive($event.member, $event.active)" (renew)="renew($event)" />
        </app-page-state>
      </nz-card>

      <app-invite-staff-drawer [open]="inviteOpen()" (closed)="inviteOpen.set(false)" (invited)="onInvited($event)" />
      <app-invite-link-modal [invite]="link()" (closed)="link.set(null)" />
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StaffPageComponent {
  private readonly staffService = inject(StaffService);
  private readonly messages = inject(NzMessageService);
  private readonly auth = inject(AuthStateService);

  readonly staff = remoteData<StaffMember[]>(() => this.staffService.list(), []);
  readonly currentUserId = computed(() => this.auth.user()?.id ?? null);
  readonly inviteOpen = signal(false);
  readonly link = signal<StaffInvite | null>(null);
  readonly busyId = signal<string | null>(null);

  onInvited(invite: StaffInvite): void {
    this.inviteOpen.set(false);
    this.link.set(invite);
    this.staff.reload();
  }

  changeRole(member: StaffMember, role: StaffRole): void {
    this.run(member, this.staffService.changeRole(member.id, role), () =>
      this.messages.success(`${member.firstName} is now ${role === 'ClinicAdmin' ? 'an admin' : 'a receptionist'}.`));
  }

  setActive(member: StaffMember, active: boolean): void {
    this.run(member, this.staffService.setActive(member.id, active), () =>
      this.messages.success(active ? `${member.firstName} can sign in again.` : `${member.firstName} can no longer sign in.`));
  }

  renew(member: StaffMember): void {
    this.busyId.set(member.id);
    this.staffService.renewInvite(member.id).pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: (invite) => {
        this.link.set(invite);
        this.staff.reload();
      }
    });
  }

  /** Runs a staff change; always reloads so the table matches the server (also after a refused change). */
  private run(member: StaffMember, request: Observable<StaffMember>, done: () => void): void {
    this.busyId.set(member.id);
    request.pipe(finalize(() => {
      this.busyId.set(null);
      this.staff.reload();
    })).subscribe({ next: done });
  }
}
