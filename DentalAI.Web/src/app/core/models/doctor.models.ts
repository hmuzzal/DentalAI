export interface Doctor {
  id: string;
  clinicId: string;
  firstName: string;
  lastName: string;
  specialty: string;
  phone: string;
  email: string;
  /** IANA zone of the doctor's clinic; booking for this doctor uses it. */
  clinicTimeZone?: string | null;
}
