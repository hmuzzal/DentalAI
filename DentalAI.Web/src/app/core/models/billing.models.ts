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
