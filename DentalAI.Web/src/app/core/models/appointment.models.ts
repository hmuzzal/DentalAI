export type AppointmentStatus = 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled' | 'NoShow';

export interface Appointment {
  id: string;
  clinicId: string;
  doctorId: string;
  doctorName: string;
  patientId: string;
  patientName: string;
  serviceId: string;
  serviceName: string;
  startTime: string;
  endTime: string;
  status: AppointmentStatus;
  notes?: string | null;
  /** IANA zone of the appointment's clinic; show its times in this zone. */
  clinicTimeZone?: string | null;
  /** "Portal" when the patient booked it online. */
  source?: 'Staff' | 'Portal';
}

export interface CreateAppointmentRequest {
  doctorId: string;
  patientId: string;
  serviceId: string;
  /** UTC instant, e.g. an AvailableSlot.startTimeUtc. */
  startTime: string;
  notes?: string | null;
}

export interface RescheduleAppointmentRequest {
  /** New UTC start, e.g. an AvailableSlot.startTimeUtc. */
  startTime: string;
  notes?: string | null;
}
