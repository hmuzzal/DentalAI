import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { formatMoney } from '../../shared/money.pipe';

export interface RevenueBar {
  /** "yyyy-MM-dd" */
  key: string;
  /** Short axis/tooltip label, e.g. "Oct 5". */
  label: string;
  value: number;
}

/** Round a step up to 1, 2, 2.5 or 5 × 10^n so axis ticks are clean numbers. */
function niceStep(raw: number): number {
  if (raw <= 0) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const normalised = raw / magnitude;
  const nice = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 2.5 ? 2.5 : normalised <= 5 ? 5 : 10;
  return nice * magnitude;
}

const TICK_COUNT = 4;

/**
 * Single-series column chart (one value per day). Built in plain HTML:
 * columns ≤ 24px with 4px rounded tops on a square baseline, hairline gridlines,
 * a tooltip on hover/focus, and a direct label on the highest column only.
 */
@Component({
  selector: 'app-revenue-chart',
  standalone: true,
  template: `
    <div class="chart" role="img" [attr.aria-label]="ariaLabel()">
      <div class="chart__axis" aria-hidden="true">
        @for (tick of ticksDesc(); track tick) { <span>{{ tickLabel(tick) }}</span> }
      </div>

      <div class="chart__plot">
        <div class="chart__grid" aria-hidden="true">
          @for (tick of ticksDesc(); track tick) { <span></span> }
        </div>

        <div class="chart__bars">
          @for (bar of bars(); track bar.key; let i = $index) {
            <div class="slot" tabindex="0" [attr.aria-label]="bar.label + ': ' + money(bar.value)"
                 (pointerenter)="active.set(i)" (pointerleave)="active.set(null)"
                 (focus)="active.set(i)" (blur)="active.set(null)">
              @if (bar.value > 0) {
                <div class="bar" [class.bar--active]="active() === i" [style.height.%]="pct(bar.value)">
                  @if (i === peakIndex()) { <span class="bar__label">{{ compact(bar.value) }}</span> }
                </div>
              }
              @if (active() === i) {
                <div class="tooltip" [class.tooltip--left]="i > bars().length * 0.7" [class.tooltip--right]="i < bars().length * 0.3" role="tooltip">
                  <strong>{{ money(bar.value) }}</strong>
                  <span>{{ bar.label }}</span>
                </div>
              }
            </div>
          }
        </div>

        <div class="chart__x" aria-hidden="true">
          @for (bar of bars(); track bar.key; let i = $index; let last = $last) {
            <span>{{ i % 7 === 0 || last ? bar.label : '' }}</span>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    /* Top padding leaves room for the peak label when the highest column reaches the top tick. */
    .chart { display: grid; grid-template-columns: auto 1fr; gap: 8px; padding-top: 20px; }
    .chart__axis { display: flex; flex-direction: column; justify-content: space-between; height: 180px;
      margin-top: -7px; margin-bottom: 22px; text-align: right; font-size: 12px; color: var(--da-ink-3);
      font-variant-numeric: tabular-nums; }
    .chart__axis span { line-height: 14px; }
    .chart__plot { position: relative; min-width: 0; }
    .chart__grid { position: absolute; inset: 0 0 22px 0; display: flex; flex-direction: column; justify-content: space-between; pointer-events: none; }
    .chart__grid span { height: 1px; background: var(--da-grid); }
    .chart__grid span:last-child { background: var(--da-border); }
    .chart__bars { position: relative; display: flex; align-items: flex-end; gap: 2px; height: 180px; }
    .slot { position: relative; flex: 1 1 0; height: 100%; display: flex; align-items: flex-end; justify-content: center; outline: none; min-width: 0; }
    .slot:focus-visible { box-shadow: inset 0 0 0 2px var(--da-brand-soft); border-radius: 4px; }
    .bar { position: relative; width: 100%; max-width: 24px; min-height: 2px; background: var(--da-data-1); border-radius: 4px 4px 0 0; transition: filter 0.12s; }
    .bar--active { filter: brightness(1.15); }
    .bar__label { position: absolute; bottom: 100%; left: 50%; transform: translateX(-50%); margin-bottom: 4px;
      font-size: 12px; font-weight: 600; color: var(--da-ink-2); white-space: nowrap; }
    /* Anchored to the top of the plot above the hovered column; flips inward near the edges. */
    .tooltip { position: absolute; top: 0; left: 50%; transform: translate(-50%, calc(-100% - 6px)); z-index: 2; pointer-events: none;
      display: grid; gap: 2px; padding: 6px 10px; border-radius: var(--da-radius-sm); white-space: nowrap;
      background: var(--da-surface); border: 1px solid var(--da-border); box-shadow: 0 4px 12px rgba(18, 52, 59, 0.12); }
    .tooltip--left { left: auto; right: 0; transform: translate(0, calc(-100% - 6px)); }
    .tooltip--right { left: 0; transform: translate(0, calc(-100% - 6px)); }
    .tooltip strong { font-size: 14px; color: var(--da-ink); }
    .tooltip span { font-size: 12px; color: var(--da-ink-2); }
    .chart__x { display: flex; gap: 2px; height: 22px; padding-top: 6px; }
    .chart__x span { flex: 1 1 0; min-width: 0; overflow: visible; white-space: nowrap; font-size: 12px; color: var(--da-ink-3); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RevenueChartComponent {
  readonly bars = input.required<RevenueBar[]>();
  /** ISO 4217 code of the clinic's currency. */
  readonly currency = input('USD');
  readonly active = signal<number | null>(null);

  private readonly step = computed(() => niceStep(Math.max(0, ...this.bars().map((b) => b.value)) / TICK_COUNT));
  private readonly max = computed(() => this.step() * TICK_COUNT);

  readonly ticksDesc = computed(() =>
    Array.from({ length: TICK_COUNT + 1 }, (_, i) => this.step() * (TICK_COUNT - i))
  );

  readonly peakIndex = computed(() => {
    let peak = -1;
    let peakValue = 0;
    this.bars().forEach((bar, i) => {
      if (bar.value > peakValue) {
        peak = i;
        peakValue = bar.value;
      }
    });
    return peak;
  });

  readonly ariaLabel = computed(() => {
    const bars = this.bars();
    const total = bars.reduce((sum, bar) => sum + bar.value, 0);
    return `Daily revenue, ${bars[0]?.label ?? ''} to ${bars.at(-1)?.label ?? ''}. Total ${this.money(total)}. Use the table view for every value.`;
  });

  pct(value: number): number {
    return (value / this.max()) * 100;
  }

  money(value: number): string {
    return formatMoney(value, this.currency(), { wholeUnits: true });
  }

  compact(value: number): string {
    return value >= 10_000 ? formatMoney(value, this.currency(), { compact: true }) : this.money(value);
  }

  tickLabel(value: number): string {
    // One style per axis: compact once the axis reaches 10K.
    return this.max() >= 10_000 ? formatMoney(value, this.currency(), { compact: true }) : this.money(value);
  }
}
