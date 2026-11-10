import { AppointmentStatus } from './appointment.models';
import { InvoiceStatus, PaymentMethod } from './billing.models';
import { ContactMethod } from './patient.models';

/** The clinic's portal as shown before and after sign-in, with its booking rules. */
export interface PortalClinic {
  name: string;
  slug: string;
  phone: string | null;
  address: string | null;
  timeZone: string;
  allowNewPatients: boolean;
  minNoticeHours: number;
  maxDaysAhead: number;
  cancelCutoffHours: number;
}

export type PortalChannel = 'Email' | 'Sms';

export interface PortalStartResponse {
  channel: PortalChannel;
  /** Normalised: lowercase email or E.164 phone. */
  contact: string;
  codeValidMinutes: number;
}

export interface PortalPatientChoice {
  id: string;
  /** First name and last initial. */
  displayName: string;
}

export interface PortalVerifyResponse {
  ticket: string;
  ticketExpiresAtUtc: string;
  channel: PortalChannel;
  contact: string;
  patients: PortalPatientChoice[];
  canRegister: boolean;
}

export interface PortalRegisterRequest {
  ticket: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  phone: string | null;
  email: string | null;
  smsOptIn: boolean;
}

export interface PortalPatientSummary {
  id: string;
  firstName: string;
  lastName: string;
}

export interface PortalSession {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  patient: PortalPatientSummary;
  clinic: PortalClinic;
}

export interface PortalProfile {
  id: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string | null;
  phone: string;
  email: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  preferredContactMethod: ContactMethod;
  smsOptIn: boolean;
  clinic: PortalClinic;
}

export interface PortalProfileRequest {
  phone: string;
  email: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  preferredContactMethod: ContactMethod;
  smsOptIn: boolean;
}

export interface PortalService {
  id: string;
  name: string;
  description: string | null;
  durationMinutes: number;
}

export interface PortalDoctor {
  id: string;
  /** "Dr. Jane Smith" */
  name: string;
  specialty: string | null;
}

export type AppointmentSource = 'Staff' | 'Portal';

export interface PortalAppointment {
  id: string;
  doctorId: string;
  doctorName: string;
  serviceId: string;
  serviceName: string;
  startTime: string;
  endTime: string;
  status: AppointmentStatus;
  source: AppointmentSource;
  timeZone: string;
  canCancel: boolean;
  canReschedule: boolean;
  changeDeadlineUtc: string;
}

export interface PortalInvoiceSummary {
  id: string;
  number: string | null;
  status: InvoiceStatus;
  issueDate: string | null;
  dueDate: string | null;
  isOverdue: boolean;
  currency: string;
  total: number;
  amountPaid: number;
  balance: number;
}

export interface PortalInvoiceList {
  items: PortalInvoiceSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  outstanding: { currency: string; amount: number }[];
}

export interface PortalInvoice {
  id: string;
  number: string | null;
  status: InvoiceStatus;
  issueDate: string | null;
  dueDate: string | null;
  isOverdue: boolean;
  currency: string;
  currencyDecimals: number;
  subtotal: number;
  discountAmount: number;
  taxRatePercent: number;
  taxAmount: number;
  total: number;
  insuranceEstimate: number;
  patientResponsibility: number;
  amountPaid: number;
  balance: number;
  notes: string | null;
  lines: { description: string; tooth: string | null; quantity: number; unitPrice: number; lineTotal: number }[];
  payments: { amount: number; method: PaymentMethod; receivedAtUtc: string; refundedAmount: number }[];
}

/** Staff side: Settings › Patient portal. */
export interface PortalSettings {
  enabled: boolean;
  slug: string;
  /** "/p/{slug}" */
  path: string;
  allowNewPatients: boolean;
  autoConfirm: boolean;
  minNoticeHours: number;
  maxDaysAhead: number;
  cancelCutoffHours: number;
  maxUpcomingAppointments: number;
}

export type PortalSettingsRequest = Omit<PortalSettings, 'path'>;
