import { HttpContextToken } from '@angular/common/http';

/**
 * Set to true on a request whose caller shows its own error message (e.g. the login form),
 * so the global error interceptor doesn't show a second one.
 */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);
