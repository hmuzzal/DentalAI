import { PaymentMethod } from './billing.models';

export type ReportGranularity = 'Day' | 'Week' | 'Month';

export interface ReportKpis {
  /** Issued invoices (not void) by issue date: subtotal minus discounts, before tax. Null without money access. */
  production: number | null;
  /** Payments received minus refunds. */
  collections: number | null;
  booked: number;
  completed: number;
  cancelled: number;
  noShows: number;
  /** 0–1; null when there were no completed visits or no-shows. */
  noShowRate: number | null;
  /** 0–1; null when nothing was booked. */
  cancellationRate: number | null;
  patientsSeen: number;
  newPatients: number;
  productionPerVisit: number | null;
}

/** Dates are clinic-local "yyyy-MM-dd". */
export interface ReportPeriod {
  start: string;
  end: string;
  production: number | null;
  collections: number | null;
  completed: number;
  newPatients: number;
}

export interface DoctorReportRow {
  doctorId: string | null;
  name: string;
  booked: number;
  completed: number;
  cancelled: number;
  noShows: number;
  production: number | null;
  collections: number | null;
}

export interface ServiceReportRow {
  serviceId: string | null;
  name: string;
  quantity: number;
  amount: number | null;
}

export interface PaymentMethodRow {
  method: PaymentMethod;
  payments: number;
  amount: number;
}

export interface PracticeReport {
  from: string;
  to: string;
  previousFrom: string;
  previousTo: string;
  currency: string;
  timeZone: string;
  granularity: ReportGranularity;
  current: ReportKpis;
  previous: ReportKpis;
  periods: ReportPeriod[];
  doctors: DoctorReportRow[];
  services: ServiceReportRow[];
  paymentMethods: PaymentMethodRow[];
  /** Invoices in another currency: left out of the money figures. */
  otherCurrencyInvoices: number;
  /** False for receptionists: money figures are null, payment methods empty, no "no appointment linked" doctor row. */
  includesMoney: boolean;
}

export interface AgingBuckets {
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
}

export interface ReceivablePatient {
  patientId: string;
  patientName: string;
  openInvoices: number;
  oldestDueDate: string | null;
  buckets: AgingBuckets;
}

export interface ReceivablesReport {
  asOf: string;
  currency: string;
  totals: AgingBuckets;
  patients: ReceivablePatient[];
  otherCurrencyInvoices: number;
}

export const AGING_COLUMNS: { key: keyof Omit<AgingBuckets, 'total'>; label: string }[] = [
  { key: 'current', label: 'Not yet due' },
  { key: 'days1To30', label: '1–30 days' },
  { key: 'days31To60', label: '31–60 days' },
  { key: 'days61To90', label: '61–90 days' },
  { key: 'over90', label: 'Over 90 days' }
];
