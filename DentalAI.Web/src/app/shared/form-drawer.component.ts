import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { map } from 'rxjs';

/**
 * Side panel for an add/edit form: title, scrolling body, Cancel + Save footer, inline save error.
 * Full width on phones. Put the <form> inside:
 *
 *   <app-form-drawer [open]="open()" title="New doctor" submitLabel="Create doctor" [saving]="saving()"
 *                    [error]="error()" (closed)="close()" (submitted)="save()">
 *     <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">...</form>
 *   </app-form-drawer>
 */
@Component({
  selector: 'app-form-drawer',
  standalone: true,
  imports: [NzAlertModule, NzButtonModule, NzDrawerModule],
  template: `
    <nz-drawer [nzVisible]="open()" [nzTitle]="title()" [nzWidth]="isMobile() ? '100%' : width()"
               [nzFooter]="footer" [nzMaskClosable]="!saving()" [nzClosable]="!saving()" (nzOnClose)="close()">
      <ng-container *nzDrawerContent>
        <ng-content />
        @if (error()) {
          <nz-alert class="error" nzType="error" nzShowIcon [nzMessage]="error()!" role="alert" />
        }
      </ng-container>
    </nz-drawer>

    <ng-template #footer>
      <div class="footer">
        <button nz-button type="button" (click)="close()" [disabled]="saving()" i18n="@@common.cancel">Cancel</button>
        <button nz-button nzType="primary" type="button" (click)="submitted.emit()" [nzLoading]="saving()">
          {{ submitLabel() }}
        </button>
      </div>
    </ng-template>
  `,
  styles: [`
    .footer { display: flex; justify-content: flex-end; gap: var(--da-space-2); }
    .error { margin-top: var(--da-space-2); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FormDrawerComponent {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly submitLabel = input.required<string>();
  readonly saving = input(false);
  readonly error = input<string | null>(null);
  readonly width = input(480);
  readonly closed = output<void>();
  readonly submitted = output<void>();

  readonly isMobile = toSignal(inject(BreakpointObserver).observe('(max-width: 640px)').pipe(map((x) => x.matches)), {
    initialValue: false
  });

  close(): void {
    if (!this.saving()) this.closed.emit();
  }
}
