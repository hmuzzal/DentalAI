import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { SchedulingService } from '../core/services/scheduling.service';
import { AvailableSlot } from '../core/models/scheduling.models';

@Component({
  selector: 'app-time-slot-selector',
  standalone: true,
  imports: [DatePipe, NzButtonModule],
  template: `
    <div class="slots">
      @if (loading()) { <p>Checking availability…</p> }
      @for (slot of slots(); track slot.startTimeUtc) {
        <button nz-button [nzType]="selected()?.startTimeUtc === slot.startTimeUtc ? 'primary' : 'default'"
          (click)="select(slot)">{{ slot.startTimeUtc | date: 'shortTime' }}</button>
      } @empty {
        @if (!loading()) { <p>No available times for this date.</p> }
      }
    </div>
  `,
  styles: [`.slots{display:flex;flex-wrap:wrap;gap:.5rem}.slots p{color:#688084}`],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TimeSlotSelectorComponent {
  readonly doctorId = input.required<string>();
  readonly serviceId = input.required<string>();
  readonly date = input.required<string>();
  readonly slotSelected = output<AvailableSlot>();
  readonly slots = signal<AvailableSlot[]>([]);
  readonly selected = signal<AvailableSlot | null>(null);
  readonly loading = signal(false);
  private readonly scheduling = inject(SchedulingService);

  constructor() {
    effect(() => {
      const doctorId = this.doctorId();
      const serviceId = this.serviceId();
      const date = this.date();
      if (!doctorId || !serviceId || !date) return;
      this.loading.set(true);
      this.scheduling.getAvailableSlots(doctorId, date, serviceId).subscribe({
        next: (slots) => { this.slots.set(slots); this.loading.set(false); },
        error: () => { this.slots.set([]); this.loading.set(false); }
      });
    });
  }

  select(slot: AvailableSlot): void {
    this.selected.set(slot);
    this.slotSelected.emit(slot);
  }
}
