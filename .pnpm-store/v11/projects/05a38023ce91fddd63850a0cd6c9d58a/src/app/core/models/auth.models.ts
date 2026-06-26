export type UserRole = 'SuperAdmin' | 'ClinicAdmin' | 'Receptionist';

export interface UserProfile {
  id: string;
  clinicId: string;
  fullName: string;
  email: string;
  role: UserRole;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: UserProfile;
}

export interface LoginRequest {
  email: string;
  password: string;
}
