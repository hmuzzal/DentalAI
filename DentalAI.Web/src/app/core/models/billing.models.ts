export type ToothNumbering = 'Universal' | 'Fdi';

export const TOOTH_NUMBERING_LABELS: Record<ToothNumbering, string> = {
  Universal: 'Universal (US): 1–32, A–T for primary teeth',
  Fdi: 'FDI (international): 11–48, 51–85 for primary teeth'
};

/** GET/PUT /billing/settings. Every clinic user can read them; only admins change them. */
export interface BillingSettings {
  /** ISO 4217, e.g. "USD" or "BDT". New invoices use it; existing invoices keep theirs. */
  currency: string;
  currencyDecimals: number;
  invoicePrefix: string;
  nextInvoiceNumber: number;
  paymentTermsDays: number;
  defaultTaxRatePercent: number;
  toothNumbering: ToothNumbering;
}

export type BillingSettingsRequest = Omit<BillingSettings, 'currencyDecimals' | 'nextInvoiceNumber'>;

export interface CurrencyOption {
  code: string;
  name: string;
  decimals: number;
}

// ---- Invoices ----

export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Void';
export type PaymentMethod = 'Cash' | 'Card' | 'Check' | 'Insurance' | 'Online' | 'Other';

export const INVOICE_STATUS: Record<InvoiceStatus, { label: string; color: string }> = {
  Draft: { label: 'Draft', color: 'default' },
  Issued: { label: 'Unpaid', color: 'blue' },
  PartiallyPaid: { label: 'Partly paid', color: 'orange' },
  Paid: { label: 'Paid', color: 'green' },
  Void: { label: 'Void', color: 'default' }
};

export const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  Cash: 'Cash',
  Card: 'Card (terminal)',
  Check: 'Check',
  Insurance: 'Insurance',
  Online: 'Online',
  Other: 'Other'
};

export interface InvoiceLine {
  id?: string;
  serviceId?: string | null;
  description: string;
  procedureCode?: string | null;
  tooth?: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal?: number;
  insuranceEstimate: number;
}

export interface Refund {
  id: string;
  amount: number;
  reason: string;
  refundedAtUtc: string;
  refundedBy?: string | null;
}

export interface Payment {
  id: string;
  amount: number;
  method: PaymentMethod;
  reference?: string | null;
  receivedAtUtc: string;
  note?: string | null;
  refundedAmount: number;
  recordedBy?: string | null;
  refunds: Refund[];
}

export interface Invoice {
  id: string;
  number?: string | null;
  status: InvoiceStatus;
  patientId: string;
  patientName: string;
  appointmentId?: string | null;
  issueDate?: string | null;
  dueDate?: string | null;
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
  notes?: string | null;
  voidReason?: string | null;
  voidedAtUtc?: string | null;
  createdDate: string;
  lines: InvoiceLine[];
  payments: Payment[];
}

export interface InvoiceSummary {
  id: string;
  number?: string | null;
  status: InvoiceStatus;
  patientId: string;
  patientName: string;
  appointmentId?: string | null;
  issueDate?: string | null;
  dueDate?: string | null;
  isOverdue: boolean;
  currency: string;
  total: number;
  amountPaid: number;
  balance: number;
  createdDate: string;
}

export interface CurrencyAmount {
  currency: string;
  amount: number;
}

export interface InvoiceList {
  items: InvoiceSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  /** Money still owed, per currency. */
  outstanding: CurrencyAmount[];
}

export interface InvoiceListQuery {
  status?: InvoiceStatus | null;
  outstandingOnly?: boolean;
  patientId?: string | null;
  appointmentId?: string | null;
  search?: string;
  from?: string | null;
  to?: string | null;
  page: number;
  pageSize: number;
}

export interface InvoiceDraftRequest {
  lines: InvoiceLine[];
  discountAmount: number;
  taxRatePercent: number;
  insuranceCoveragePercent?: number | null;
  notes?: string | null;
}

export interface CreateInvoiceRequest extends Omit<InvoiceDraftRequest, 'taxRatePercent'> {
  patientId: string;
  appointmentId?: string | null;
  taxRatePercent?: number | null;
}

export interface RecordPaymentRequest {
  amount: number;
  method: PaymentMethod;
  reference?: string | null;
  receivedAtUtc?: string | null;
  note?: string | null;
}

export interface InvoiceParty {
  name: string;
  addressLine1?: string | null;
  addressLine2?: string | null;
  cityLine?: string | null;
  phone?: string | null;
  email?: string | null;
}

/** GET /invoices/{id}/document */
export interface InvoiceDocument {
  invoice: Invoice;
  clinic: InvoiceParty;
  patient: InvoiceParty;
  toothNumbering: ToothNumbering;
}
