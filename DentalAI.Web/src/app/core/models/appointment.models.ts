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
}
