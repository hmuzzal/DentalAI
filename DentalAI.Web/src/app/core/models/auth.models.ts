export type UserRole = 'SuperAdmin' | 'ClinicAdmin' | 'Receptionist';

export const USER_ROLE_LABELS: Record<UserRole, string> = {
  SuperAdmin: 'Platform admin',
  ClinicAdmin: 'Clinic admin',
  Receptionist: 'Receptionist'
};

export interface UserProfile {
  id: string;
  /** Null for platform admins, who are not tied to a clinic. */
  clinicId: string | null;
  fullName: string;
  email: string;
  role: UserRole;
  clinicName: string | null;
  /** IANA time zone of the user's clinic, e.g. "America/New_York". */
  clinicTimeZone: string | null;
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
