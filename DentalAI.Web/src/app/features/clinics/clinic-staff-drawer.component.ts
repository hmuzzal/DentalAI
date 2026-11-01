import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { finalize, map } from 'rxjs';
import { Clinic } from '../../core/models/clinic.models';
import { StaffInvite, StaffMember } from '../../core/models/staff.models';
import { StaffService } from '../../core/services/staff.service';
import { InviteLinkModalComponent } from '../../shared/invite-link-modal.component';
import { InviteStaffDrawerComponent } from '../staff/invite-staff-drawer.component';
import { StaffTableComponent } from '../staff/staff-table.component';

/**
 * A clinic's staff accounts, for platform admins: invite the clinic's admins, send a new link, deactivate or
 * reactivate accounts. Role changes and inviting receptionists are left to the clinic's own admins.
 */
@Component({
  selector: 'app-clinic-staff-drawer',
  standalone: true,
  imports: [
    NzAlertModule, NzButtonModule, NzDrawerModule, NzIconModule, StaffTableComponent, InviteStaffDrawerComponent,
    InviteLinkModalComponent
  ],
  template: `
    <nz-drawer [nzVisible]="!!clinic()" [nzTitle]="(clinic()?.name ?? '') + ' · Staff'" [nzWidth]="isMobile() ? '100%' : 760"
               (nzOnClose)="closed.emit()">
      <ng-container *nzDrawerContent>
        <div class="bar">
          <p class="muted">Accounts only. Patients and appointments stay private to the clinic.</p>
          <button nz-button nzType="primary" (click)="inviteOpen.set(true)">
            <span nz-icon nzType="plus"></span> Invite admin
          </button>
        </div>
        @if (loadError()) {
          <nz-alert nzType="error" nzShowIcon nzMessage="We could not load this clinic's staff." [nzAction]="retry" />
          <ng-template #retry><button nz-button nzSize="small" (click)="load()">Try again</button></ng-template>
        } @else if (!loading() && !members().length) {
          <nz-alert nzType="warning" nzShowIcon nzMessage="This clinic has no staff yet."
                    nzDescription="Invite the clinic's first admin. They can then invite the rest of their team." />
        } @else {
          <app-staff-table [members]="members()" [canChangeRoles]="false" [busyId]="busyId()" [loading]="loading()"
                           (setActive)="setActive($event.member, $event.active)" (renew)="renew($event)" />
        }
      </ng-container>
    </nz-drawer>

    <app-invite-staff-drawer [open]="inviteOpen()" [clinicId]="clinic()?.id ?? null" [roles]="['ClinicAdmin']"
                             title="Invite clinic admin" (closed)="inviteOpen.set(false)" (invited)="onInvited($event)" />
    <app-invite-link-modal [invite]="link()" (closed)="link.set(null)" />
  `,
  styles: [`
    .bar { display: flex; justify-content: space-between; align-items: center; gap: var(--da-space-3); margin-bottom: var(--da-space-3); }
    .muted { color: var(--da-ink-2); margin: 0; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClinicStaffDrawerComponent {
  /** The clinic whose staff to show; null closes the drawer. */
  readonly clinic = input<Clinic | null>(null);
  readonly closed = output<void>();

  private readonly staff = inject(StaffService);
  private readonly messages = inject(NzMessageService);
  readonly isMobile = toSignal(inject(BreakpointObserver).observe('(max-width: 640px)').pipe(map((x) => x.matches)), {
    initialValue: false
  });

  readonly members = signal<StaffMember[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly busyId = signal<string | null>(null);
  readonly inviteOpen = signal(false);
  readonly link = signal<StaffInvite | null>(null);

  constructor() {
    effect(() => {
      const clinic = this.clinic();
      untracked(() => {
        this.members.set([]);
        if (clinic) this.load();
      });
    });
  }

  load(): void {
    const clinic = this.clinic();
    if (!clinic) return;
    this.loading.set(true);
    this.loadError.set(false);
    this.staff.list(clinic.id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (members) => this.members.set(members),
      error: () => this.loadError.set(true)
    });
  }

  onInvited(invite: StaffInvite): void {
    this.inviteOpen.set(false);
    this.link.set(invite);
    this.load();
  }

  setActive(member: StaffMember, active: boolean): void {
    const clinic = this.clinic();
    if (!clinic) return;
    this.busyId.set(member.id);
    this.staff.setActive(member.id, active, clinic.id).pipe(finalize(() => {
      this.busyId.set(null);
      this.load();
    })).subscribe({
      next: () => this.messages.success(active ? `${member.firstName} can sign in again.` : `${member.firstName} can no longer sign in.`)
    });
  }

  renew(member: StaffMember): void {
    const clinic = this.clinic();
    if (!clinic) return;
    this.busyId.set(member.id);
    this.staff.renewInvite(member.id, clinic.id).pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: (invite) => {
        this.link.set(invite);
        this.load();
      }
    });
  }
}
