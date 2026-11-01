export interface Clinic {
  id: string;
  name: string;
  slug?: string | null;
  address?: string | null;
  phone?: string | null;
  email?: string | null;
  /** IANA time-zone ID, e.g. "America/New_York". All clinic times are shown in this zone. */
  timeZone: string;
  /** ISO 3166-1 alpha-2, e.g. "US". Phone numbers typed without a country code are read in this country. */
  country: string;
  /** Minutes between bookable slot start times (5, 10, 15, 20, 30 or 60). */
  slotIntervalMinutes: number;
}

/** Body for POST /clinics and PUT /clinics/{id}. */
export interface ClinicRequest {
  name: string;
  phone: string;
  email: string;
  address: string;
  timeZone: string;
  country: string;
  slotIntervalMinutes: number;
}

/** GET /lookups/time-zones */
export interface TimeZoneOption {
  id: string;
  /** e.g. "(UTC-05:00) Eastern Time"; the offset is the zone's current one. */
  label: string;
  utcOffsetMinutes: number;
  /** "United States" or "Other". */
  group: string;
}

/** GET /lookups/countries */
export interface CountryOption {
  /** ISO 3166-1 alpha-2, e.g. "US" */
  code: string;
  name: string;
}
