import { Pipe, PipeTransform } from '@angular/core';

/**
 * Formats a stored E.164 number for display: US/Canada (+1) as (212) 555-0142, others as stored (+8801711000101).
 * Values that aren't E.164 (records saved before numbers were normalised) are shown unchanged.
 */
export function formatPhone(value: string | null | undefined): string {
  if (!value) return '';
  const nanp = /^\+1(\d{3})(\d{3})(\d{4})$/.exec(value);
  return nanp ? `(${nanp[1]}) ${nanp[2]}-${nanp[3]}` : value;
}

@Pipe({ name: 'phone', standalone: true })
export class PhonePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatPhone(value);
  }
}

/** Help text under phone fields: which country a number typed without +country code is read as. */
export function phoneCountryHint(countryCode: string | null | undefined): string {
  const code = (countryCode || 'US').toUpperCase();
  if (code === 'US') return 'US numbers need no country code. For other countries, start with + and the country code.';
  let name = code;
  try {
    name = new Intl.DisplayNames(['en'], { type: 'region' }).of(code) ?? code;
  } catch {
    // Unknown to this browser: show the code.
  }
  return `Numbers without a country code are read as ${name} numbers. For other countries, start with +.`;
}
