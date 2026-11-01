import { Pipe, PipeTransform } from '@angular/core';

const formatters = new Map<string, Intl.NumberFormat>();

/**
 * Formats an amount in a currency (ISO 4217), with that currency's usual decimals: "$1,250.00", "BDT 1,250.00",
 * "¥1,250". `compact` gives short forms for charts ("$1.3K").
 */
export function formatMoney(value: number | null | undefined, currency: string, options?: { compact?: boolean; wholeUnits?: boolean }): string {
  if (value === null || value === undefined) return '';
  const key = `${currency}|${options?.compact ? 'c' : ''}${options?.wholeUnits ? 'w' : ''}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    try {
      formatter = new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency,
        ...(options?.compact ? { notation: 'compact', maximumFractionDigits: 1 } : {}),
        ...(options?.wholeUnits ? { maximumFractionDigits: 0 } : {})
      });
    } catch {
      // Unknown to this browser: plain number with the code.
      formatter = new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 });
      const plain = formatter;
      return `${currency} ${plain.format(value)}`;
    }
    formatters.set(key, formatter);
  }
  return formatter.format(value);
}

@Pipe({ name: 'money', standalone: true })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, currency: string): string {
    return formatMoney(value, currency);
  }
}
