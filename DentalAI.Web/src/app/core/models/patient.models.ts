export interface Patient {
  id: string;
  clinicId: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phone: string;
  email?: string | null;
  dateOfBirth?: string | null;
  gender?: string | null;
  notes?: string | null;
}

/** Body for POST /patients and PUT /patients/{id}. */
export interface PatientRequest {
  clinicId: string | null;
  firstName: string;
  lastName: string;
  phone: string;
  email: string | null;
  /** yyyy-MM-dd */
  dateOfBirth: string | null;
  gender: string | null;
  notes: string | null;
}
