import { UserRole } from './auth.models';

/** Invited: link not used yet. InviteExpired: send a new link. Deactivated: can't sign in. */
export type StaffStatus = 'Active' | 'Invited' | 'InviteExpired' | 'Deactivated';
/** Roles a clinic's staff can have. */
export type StaffRole = Extract<UserRole, 'ClinicAdmin' | 'Receptionist'>;

export const STAFF_STATUS: Record<StaffStatus, { label: string; color: string }> = {
  Active: { label: 'Active', color: 'green' },
  Invited: { label: 'Invited', color: 'blue' },
  InviteExpired: { label: 'Invite expired', color: 'orange' },
  Deactivated: { label: 'Deactivated', color: 'default' }
};

export const STAFF_ROLE_LABELS: Record<StaffRole, string> = {
  ClinicAdmin: 'Admin',
  Receptionist: 'Receptionist'
};

export interface StaffMember {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string;
  role: StaffRole;
  status: StaffStatus;
  /** Set while the person hasn't accepted their invite. */
  inviteExpiresAtUtc?: string | null;
  createdDate: string;
}

/** The token is returned once; turn it into a link with inviteLink(). */
export interface StaffInvite {
  member: StaffMember;
  inviteToken: string;
  inviteExpiresAtUtc: string;
}

export interface InviteStaffRequest {
  firstName: string;
  lastName: string;
  email: string;
  role: StaffRole;
}

/** The link the invited person opens to set their password. */
export function inviteLink(token: string): string {
  return `${window.location.origin}/auth/accept-invite?token=${encodeURIComponent(token)}`;
}
