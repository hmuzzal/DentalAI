import { AppointmentStatus } from './models/appointment.models';

/** Display label and ng-zorro tag colour for each appointment status. Always show the label with the colour. */
export const APPOINTMENT_STATUS: Record<AppointmentStatus, { label: string; color: string }> = {
  Pending: { label: 'Pending', color: 'gold' },
  Confirmed: { label: 'Confirmed', color: 'blue' },
  Completed: { label: 'Completed', color: 'green' },
  Cancelled: { label: 'Cancelled', color: 'red' },
  NoShow: { label: 'No-show', color: 'volcano' }
};
