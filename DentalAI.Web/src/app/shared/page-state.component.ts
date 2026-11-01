import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { LoadStatus } from './remote-data';

/**
 * Shows a skeleton while loading, an error with "Try again", or an empty message.
 * Renders its content only when the data is ready and not empty.
 *
 *   <app-page-state [status]="doctors.status()" [empty]="!doctors.value().length"
 *                   emptyText="No doctors yet" (retry)="doctors.reload()">
 *     ...table...
 *   </app-page-state>
 */
@Component({
  selector: 'app-page-state',
  standalone: true,
  imports: [NzButtonModule, NzEmptyModule, NzIconModule, NzSkeletonModule],
  template: `
    @switch (status()) {
      @case ('loading') {
        <div class="state" aria-busy="true"><nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: rows() }" /></div>
      }
      @case ('error') {
        <div class="state state--center" role="alert">
          <strong>{{ errorText() }}</strong>
          <p class="da-muted">Check your connection, then try again.</p>
          <button nz-button (click)="retry.emit()"><span nz-icon nzType="reload"></span>Try again</button>
        </div>
      }
      @default {
        @if (empty()) {
          <div class="state state--center">
            <nz-empty [nzNotFoundContent]="emptyText()" />
          </div>
        } @else {
          <ng-content />
        }
      }
    }
  `,
  styles: [`
    .state { padding: var(--da-space-4) 0; }
    .state--center { display: grid; justify-items: center; gap: var(--da-space-2); padding: var(--da-space-6) var(--da-space-4); text-align: center; }
    .state--center p { margin: 0 0 var(--da-space-2); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PageStateComponent {
  readonly status = input.required<LoadStatus>();
  readonly empty = input(false);
  readonly emptyText = input('Nothing here yet');
  readonly errorText = input('We could not load this data.');
  readonly rows = input(4);
  readonly retry = output<void>();
}
