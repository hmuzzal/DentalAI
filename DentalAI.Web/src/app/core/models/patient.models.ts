export type ContactMethod = 'Phone' | 'Sms' | 'Email';
export type InsurancePriority = 'Primary' | 'Secondary';
export type InsuranceRelationship = 'Self' | 'Spouse' | 'Child' | 'Other';

export const CONTACT_METHOD_LABELS: Record<ContactMethod, string> = {
  Phone: 'Phone call',
  Sms: 'Text message',
  Email: 'Email'
};

export const RELATIONSHIP_LABELS: Record<InsuranceRelationship, string> = {
  Self: 'Self (patient holds the plan)',
  Spouse: 'Spouse',
  Child: 'Child',
  Other: 'Other'
};

export interface PatientInsurance {
  id?: string;
  priority: InsurancePriority;
  carrierName: string;
  memberId: string;
  groupNumber?: string | null;
  relationshipToPatient: InsuranceRelationship;
  subscriberName?: string | null;
  /** yyyy-MM-dd */
  subscriberDateOfBirth?: string | null;
}

export interface Patient {
  id: string;
  clinicId: string;
  firstName: string;
  lastName: string;
  fullName: string;
  /** E.164, e.g. +12125550142 (older records may not be normalised yet). */
  phone: string;
  /** Not a valid stored number (an old record): texts and portal sign-in by phone won't work. */
  phoneNeedsReview?: boolean;
  email?: string | null;
  /** yyyy-MM-dd */
  dateOfBirth?: string | null;
  gender?: string | null;
  notes?: string | null;

  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;

  preferredContactMethod: ContactMethod;
  smsOptIn: boolean;
  smsOptInAtUtc?: string | null;
  smsOptOutAtUtc?: string | null;

  /** Only filled by GET /patients/{id}; lists leave it empty. */
  insurances: PatientInsurance[];
}

/** Body for POST /patients and PUT /patients/{id}. */
export interface PatientRequest {
  firstName: string;
  lastName: string;
  /** Any common format; the API stores E.164. */
  phone: string;
  email: string | null;
  /** yyyy-MM-dd */
  dateOfBirth: string | null;
  gender: string | null;
  notes: string | null;

  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;

  preferredContactMethod: ContactMethod;
  smsOptIn: boolean;
  /** Replaces the patient's policies (send the existing ones to keep them). */
  insurances: PatientInsurance[];
}
