export interface DentalService {
  id: string;
  clinicId: string;
  name: string;
  description?: string | null;
  price: number;
  durationMinutes: number;
  isActive: boolean;
}
