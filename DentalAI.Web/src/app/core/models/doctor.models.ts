export interface Doctor {
  id: string;
  clinicId: string;
  fullName: string;
  specialization: string;
  workDayStart: string;
  workDayEnd: string;
  isActive: boolean;
}
