import { ChangeDetectionStrategy, Component, computed, inject, input, model, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { catchError, map, Observable, of, startWith, switchMap } from 'rxjs';
import { AvailableSlot } from '../core/models/scheduling.models';
import { SchedulingService } from '../core/services/scheduling.service';
import { formatInZone, hourInZone } from '../core/time-zone';

/** Loads open times. Defaults to the staff scheduling API; the patient portal passes its own. */
export type SlotLoader = (doctorId: string, date: string, serviceId: string, excludeAppointmentId: string | null) => Observable<AvailableSlot[]>;

type SlotState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; slots: AvailableSlot[] };

const PARTS_OF_DAY = [
  { key: 'morning', label: 'Morning', test: (hour: number) => hour < 12 },
  { key: 'afternoon', label: 'Afternoon', test: (hour: number) => hour >= 12 && hour < 17 },
  { key: 'evening', label: 'Evening', test: (hour: number) => hour >= 17 }
] as const;

/**
 * Lists a doctor's free start times for one service on one clinic-local date.
 * Times are shown in `timeZone` (the clinic's), not the browser's.
 */
@Component({
  selector: 'app-time-slot-selector',
  standalone: true,
  imports: [NzButtonModule, NzSkeletonModule],
  template: `
    @switch (state().status) {
      @case ('idle') {
        <p class="slots__hint">Choose a doctor, service and date to see open times.</p>
      }
      @case ('loading') {
        <nz-skeleton [nzActive]="true" [nzTitle]="false" [nzParagraph]="{ rows: 2 }" />
      }
      @case ('error') {
        <p class="slots__hint">We could not load open times.
          <button nz-button nzType="link" class="slots__retry" (click)="retry()">Try again</button>
        </p>
      }
      @case ('ready') {
        @for (group of groups(); track group.key) {
          <div class="slots__group" role="group" [attr.aria-label]="group.label">
            <span class="slots__label">{{ group.label }}</span>
            <div class="slots__grid">
              @for (slot of group.slots; track slot.startTimeUtc) {
                <button nz-button type="button" class="slots__slot"
                        [nzType]="selected()?.startTimeUtc === slot.startTimeUtc ? 'primary' : 'default'"
                        [attr.aria-pressed]="selected()?.startTimeUtc === slot.startTimeUtc"
                        (click)="selected.set(slot)">{{ label(slot) }}</button>
              }
            </div>
          </div>
        } @empty {
          <p class="slots__hint">No open times on this date. Try another day.</p>
        }
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .slots__group + .slots__group { margin-top: var(--da-space-3); }
    .slots__label { display: block; margin-bottom: var(--da-space-1); font-size: 12px; font-weight: 600; color: var(--da-ink-3);
      text-transform: uppercase; letter-spacing: 0.06em; }
    .slots__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(88px, 1fr)); gap: var(--da-space-2); }
    .slots__slot { font-variant-numeric: tabular-nums; }
    .slots__hint { margin: 0; color: var(--da-ink-3); }
    .slots__retry { padding: 0 4px; height: auto; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TimeSlotSelectorComponent {
  private readonly scheduling = inject(SchedulingService);

  readonly doctorId = input<string | null>(null);
  readonly serviceId = input<string | null>(null);
  /** Clinic-local date, "yyyy-MM-dd". */
  readonly date = input<string | null>(null);
  readonly timeZone = input.required<string>();
  /** When rescheduling: the appointment being moved, so it doesn't block its own slots. */
  readonly excludeAppointmentId = input<string | null>(null);
  /** Bump to force a reload (e.g. after a booking conflict). */
  readonly refresh = input(0);
  readonly selected = model<AvailableSlot | null>(null);
  readonly loader = input<SlotLoader | null>(null);

  private readonly retries = signal(0);

  private readonly request = computed(() => ({
    doctorId: this.doctorId(),
    serviceId: this.serviceId(),
    date: this.date(),
    excludeAppointmentId: this.excludeAppointmentId(),
    refresh: this.refresh() + this.retries(),
    loader: this.loader()
  }));

  readonly state = toSignal(
    toObservable(this.request).pipe(
      switchMap(({ doctorId, serviceId, date, excludeAppointmentId, loader }) => {
        if (!doctorId || !serviceId || !date) {
          return of<SlotState>({ status: 'idle' });
        }
        const load: SlotLoader = loader ?? ((d, day, s, e) => this.scheduling.getAvailableSlots(d, day, s, e));
        return load(doctorId, date, serviceId, excludeAppointmentId).pipe(
          map((slots): SlotState => ({ status: 'ready', slots })),
          catchError(() => of<SlotState>({ status: 'error' })),
          startWith<SlotState>({ status: 'loading' })
        );
      })
    ),
    { initialValue: { status: 'idle' } as SlotState }
  );

  readonly groups = computed(() => {
    const state = this.state();
    if (state.status !== 'ready') return [];
    const zone = this.timeZone();
    return PARTS_OF_DAY
      .map((part) => ({ ...part, slots: state.slots.filter((slot) => part.test(hourInZone(slot.startTimeUtc, zone))) }))
      .filter((group) => group.slots.length > 0);
  });

  label(slot: AvailableSlot): string {
    return formatInZone(slot.startTimeUtc, this.timeZone(), { hour: 'numeric', minute: '2-digit' });
  }

  retry(): void {
    this.retries.update((value) => value + 1);
  }
}
