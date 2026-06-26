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
  scheduledStartUtc: string;
  scheduledEndUtc: string;
  status: AppointmentStatus;
  notes?: string | null;
}
