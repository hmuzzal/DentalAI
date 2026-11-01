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
}

export interface CalendarResponse {
  fromDate: string;
  toDate: string;
  appointments: CalendarAppointment[];
}

export interface AvailableSlot {
  startTimeUtc: string;
  endTimeUtc: string;
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
