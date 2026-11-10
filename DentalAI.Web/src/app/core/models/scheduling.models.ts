import { AppointmentStatus } from './appointment.models';

export type CalendarView = 'day' | 'week' | 'month';

export interface CalendarAppointment {
  id: string;
  clinicId: string | null;
  doctorId: string;
  doctorName: string;
  patientId: string;
  patientName: string;
  serviceId: string;
  serviceName: string;
  startTimeUtc: string;
  endTimeUtc: string;
  status: AppointmentStatus;
  /** IANA zone of the appointment's clinic; show its times in this zone. */
  clinicTimeZone?: string | null;
  /** "Portal" when the patient booked it online. */
  source?: 'Staff' | 'Portal';
}

export interface CalendarResponse {
  fromDate: string;
  toDate: string;
  appointments: CalendarAppointment[];
}

export interface AvailableSlot {
  startTimeUtc: string;
  endTimeUtc: string;
  /** IANA zone of the doctor's clinic; show the slot in this zone. */
  clinicTimeZone?: string | null;
}

export interface DoctorSchedulePeriod {
  id?: string;
  dayOfWeek: string;
  startTime: string;
  endTime: string;
  isWorking: boolean;
}

export interface DoctorBreakPeriod {
  id?: string;
  dayOfWeek: string;
  startTime: string;
  endTime: string;
}

export interface DoctorLeavePeriod {
  id?: string;
  startDate: string;
  endDate: string;
  reason: string;
}

export interface DoctorAvailability {
  doctorId: string;
  schedules: DoctorSchedulePeriod[];
  breaks: DoctorBreakPeriod[];
  leaves: DoctorLeavePeriod[];
}
