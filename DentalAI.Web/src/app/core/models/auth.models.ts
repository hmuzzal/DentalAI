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
  /** ISO 3166-1 alpha-2 country of the clinic, e.g. "US". */
  clinicCountry?: string | null;
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

/**
 * Roles that work inside a clinic and may see its patients, appointments, doctors and services.
 * Platform admins (SuperAdmin) are deliberately not included: they manage clinics, not clinic data.
 */
export const CLINIC_STAFF: UserRole[] = ['ClinicAdmin', 'Receptionist'];

/** GET /auth/invite?token=... */
export interface InviteDetails {
  firstName: string;
  lastName: string;
  email: string;
  clinicName: string;
  role: UserRole;
  expiresAtUtc: string;
}

/** Mirrors the API's password policy for staff accounts. */
export const PASSWORD_HELP = 'At least 10 characters, with at least one letter and one number.';
export const PASSWORD_PATTERN = /^(?=.*\p{L})(?=.*\d).{10,128}$/u;
