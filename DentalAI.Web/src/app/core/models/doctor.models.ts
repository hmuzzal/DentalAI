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

/** Body for POST /doctors and PUT /doctors/{id}. Clinic users' clinicId is ignored by the API. */
export interface DoctorRequest {
  clinicId: string | null;
  firstName: string;
  lastName: string;
  specialty: string;
  phone: string;
  email: string;
}
