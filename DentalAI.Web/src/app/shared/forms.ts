import { HttpContext, HttpErrorResponse } from '@angular/common/http';
import { FormGroup } from '@angular/forms';
import { describeHttpError } from '../core/http/error.interceptor';
import { SILENT_ERRORS } from '../core/http/http-context';

/** HTTP options for a save whose form shows its own error, so the global toast stays quiet. */
export const silentErrors = () => ({ context: new HttpContext().set(SILENT_ERRORS, true) });

/** Shows every validation message at once, e.g. when the user presses Save on an incomplete form. */
export function touchAll(form: FormGroup): void {
  Object.values(form.controls).forEach((control) => {
    control.markAsDirty();
    control.markAsTouched();
    control.updateValueAndValidity({ onlySelf: true });
  });
}

/** Every message the API returned for a failed save, joined for an inline alert. */
export function saveErrorText(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong. Please try again.';
  }
  const errors: unknown = error.error?.errors;
  return Array.isArray(errors) && errors.length ? errors.join(' ') : describeHttpError(error);
}

/** Trimmed text, or null when empty (for optional API fields). */
export function optional(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

/** Case-insensitive "every word appears somewhere" match, used by the client-side list searches. */
export function matchesSearch(search: string, ...fields: (string | null | undefined)[]): boolean {
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const haystack = fields.filter(Boolean).join(' ').toLowerCase();
  return words.every((word) => haystack.includes(word));
}

/** US phone / E.164-ish pattern shared with the API validators. */
export const PHONE_PATTERN = /^\+?[0-9\s\-().]{7,20}$/;
