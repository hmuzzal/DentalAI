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
