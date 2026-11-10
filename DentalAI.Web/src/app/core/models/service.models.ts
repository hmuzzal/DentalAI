export interface DentalService {
  id: string;
  clinicId: string;
  name: string;
  description?: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
  /** Patients can book it on the clinic's portal. */
  bookableOnline: boolean;
}

/** Body for POST /services and PUT /services/{id}. Always for the signed-in user's clinic. */
export interface ServiceRequest {
  name: string;
  description: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
  /** Patients can book it on the clinic's portal. */
  bookableOnline: boolean;
}
