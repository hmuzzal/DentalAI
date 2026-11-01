export interface DentalService {
  id: string;
  clinicId: string;
  name: string;
  description?: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
}

/** Body for POST /services and PUT /services/{id}. Always for the signed-in user's clinic. */
export interface ServiceRequest {
  name: string;
  description: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
}
