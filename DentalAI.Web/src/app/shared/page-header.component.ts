import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Standard page title row. Put page actions (buttons, filters) inside the tag:
 *   <app-page-header title="Patients" subtitle="..."><button nz-button>Add</button></app-page-header>
 */
@Component({
  selector: 'app-page-header',
  standalone: true,
  template: `
    <header class="page-header">
      <div class="page-header__text">
        @if (eyebrow()) { <p class="page-header__eyebrow">{{ eyebrow() }}</p> }
        <h1>{{ title() }}</h1>
        @if (subtitle()) { <p class="page-header__subtitle">{{ subtitle() }}</p> }
      </div>
      <div class="page-header__actions"><ng-content /></div>
    </header>
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: flex-end; gap: var(--da-space-4); flex-wrap: wrap; }
    .page-header__text { min-width: 0; }
    .page-header__eyebrow { margin: 0 0 2px; text-transform: uppercase; letter-spacing: 0.08em; font-size: 12px; font-weight: 600; color: var(--da-brand); }
    h1 { margin: 0; font-size: 24px; line-height: 32px; font-weight: 600; color: var(--da-ink); }
    .page-header__subtitle { margin: 4px 0 0; color: var(--da-ink-2); }
    .page-header__actions { display: flex; align-items: center; gap: var(--da-space-2); flex-wrap: wrap; }
    .page-header__actions:empty { display: none; }
    @media (max-width: 640px) {
      .page-header { align-items: stretch; flex-direction: column; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly eyebrow = input<string>();
  readonly subtitle = input<string>();
}
