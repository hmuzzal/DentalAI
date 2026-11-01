export interface DentalService {
  id: string;
  clinicId: string;
  name: string;
  description?: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
}

/** Body for POST /services and PUT /services/{id}. clinicId is used only when a super admin creates one. */
export interface ServiceRequest {
  clinicId: string | null;
  name: string;
  description: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
}
