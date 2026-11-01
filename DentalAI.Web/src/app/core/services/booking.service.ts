import { inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, skip } from 'rxjs';
import { NzMessageService } from 'ng-zorro-antd/message';
import { Appointment } from '../models/appointment.models';
import { AppointmentsService } from './appointments.service';

export interface BookingDefaults {
  doctorId?: string | null;
  /** Clinic-local calendar date, "yyyy-MM-dd". */
  date?: string | null;
}

/** The fields the drawer needs to move an appointment (Appointment and CalendarAppointment both fit). */
export interface ReschedulableAppointment {
  id: string;
  doctorId: string;
  doctorName: string;
  patientName: string;
  serviceId: string;
  serviceName: string;
  /** Current UTC start. */
  startTime: string;
}

export type BookingMode =
  | { kind: 'book'; defaults: BookingDefaults }
  | { kind: 'reschedule'; appointment: ReschedulableAppointment };

/** Statuses that can still be moved; mirrors the API rule. */
export const RESCHEDULABLE_STATUSES = ['Pending', 'Confirmed'] as const;

/** Statuses that can still be cancelled; mirrors the API's status-transition rule. */
export const CANCELLABLE_STATUSES = ['Pending', 'Confirmed'] as const;

/**
 * Opens the app-wide booking drawer (hosted by the app shell), in "book" or "reschedule" mode,
 * and tells pages when an appointment was booked or moved so they can refresh.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly appointments = inject(AppointmentsService);
  private readonly messages = inject(NzMessageService);

  readonly isOpen = signal(false);
  readonly mode = signal<BookingMode>({ kind: 'book', defaults: {} });
  /** Last appointment booked, rescheduled or cancelled in this session; pages watch it to reload. */
  readonly lastChanged = signal<Appointment | null>(null);

  open(defaults: BookingDefaults = {}): void {
    this.mode.set({ kind: 'book', defaults });
    this.isOpen.set(true);
  }

  reschedule(appointment: ReschedulableAppointment): void {
    this.mode.set({ kind: 'reschedule', appointment });
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }

  notifyChanged(appointment: Appointment): void {
    this.lastChanged.set(appointment);
  }

  /** Cancels an appointment, confirms it in a toast and refreshes listening pages. */
  cancel(appointment: { id: string; patientName: string }): void {
    this.appointments.cancel(appointment.id).subscribe({
      next: (cancelled) => {
        this.messages.success(`Cancelled the appointment for ${appointment.patientName}.`);
        this.notifyChanged(cancelled);
      }
      // Errors: the global interceptor already shows a message.
    });
  }
}

/**
 * Runs `callback` whenever an appointment is booked, rescheduled or cancelled while the calling component is alive.
 * Call from a constructor or field initializer (needs an injection context).
 */
export function onAppointmentsChanged(callback: (appointment: Appointment) => void): void {
  const booking = inject(BookingService);
  toObservable(booking.lastChanged)
    .pipe(skip(1), filter((appointment): appointment is Appointment => appointment !== null), takeUntilDestroyed())
    .subscribe(callback);
}
