import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { inviteLink, StaffInvite } from '../core/models/staff.models';

/**
 * Shows a freshly created invite link once, with a Copy button. The link can't be shown again later (only its
 * hash is stored), so the admin copies it now and sends it to the person. Email delivery comes in Phase 3.
 */
@Component({
  selector: 'app-invite-link-modal',
  standalone: true,
  imports: [DatePipe, NzAlertModule, NzButtonModule, NzIconModule, NzInputModule, NzModalModule],
  template: `
    <nz-modal [nzVisible]="!!invite()" nzTitle="Send this invite link" [nzFooter]="footer" (nzOnCancel)="closed.emit()"
              [nzWidth]="560">
      <ng-container *nzModalContent>
        @if (invite(); as current) {
          <p>
            Send this link to <strong>{{ current.member.fullName }}</strong> ({{ current.member.email }}).
            They open it to set their own password. It works once and expires
            <strong>{{ current.inviteExpiresAtUtc | date: 'MMM d, h:mm a' }}</strong>.
          </p>
          <nz-input-group [nzSuffix]="copyButton">
            <input nz-input [value]="link()" readonly aria-label="Invite link" (focus)="select($event)" />
          </nz-input-group>
          <ng-template #copyButton>
            <button nz-button nzType="link" nzSize="small" (click)="copy()">{{ copied() ? 'Copied' : 'Copy' }}</button>
          </ng-template>
          <nz-alert class="note" nzType="info" nzShowIcon
                    nzMessage="This is the only time the link is shown. If it's lost, create a new one from the staff list." />
        }
      </ng-container>
      <ng-template #footer>
        <button nz-button nzType="primary" (click)="closed.emit()">Done</button>
      </ng-template>
    </nz-modal>
  `,
  styles: [`.note { margin-top: var(--da-space-3); }`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InviteLinkModalComponent {
  readonly invite = input<StaffInvite | null>(null);
  readonly closed = output<void>();

  private readonly messages = inject(NzMessageService);
  readonly copied = signal(false);
  readonly link = computed(() => {
    const invite = this.invite();
    return invite ? inviteLink(invite.inviteToken) : '';
  });

  constructor() {
    // A new link starts un-copied.
    effect(() => {
      this.invite();
      untracked(() => this.copied.set(false));
    });
  }

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link());
      this.copied.set(true);
    } catch {
      this.messages.warning('Copy failed. Select the link and copy it with Ctrl+C.');
    }
  }

  select(event: FocusEvent): void {
    (event.target as HTMLInputElement).select();
  }
}
