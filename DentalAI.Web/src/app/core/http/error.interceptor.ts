import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { NzMessageService } from 'ng-zorro-antd/message';
import { catchError, throwError } from 'rxjs';
import { AuthStateService } from '../services/auth-state.service';
import { SILENT_ERRORS } from './http-context';
import { isPortalRequest } from '../services/portal-session.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const messages = inject(NzMessageService);
  const authState = inject(AuthStateService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || req.context.get(SILENT_ERRORS)) {
        return throwError(() => error);
      }

      if (error.status === 401 && isPortalRequest(req.url)) {
        // The patient session: the auth interceptor refreshes it, or signs the patient out. Staff stay signed in.
      } else if (error.status === 401) {
        // Session expired or token invalid: send the user back to sign in.
        if (authState.isAuthenticated()) {
          messages.warning('Your session has expired. Please sign in again.');
          authState.logout();
        }
      } else {
        messages.error(describeHttpError(error));
      }

      return throwError(() => error);
    })
  );
};

/** User-facing text for a failed API call. */
export function describeHttpError(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Cannot reach the server. Check your connection and try again.';
  }

  if (error.status === 403) {
    return 'You do not have permission to do that.';
  }

  const body = error.error as { message?: string; errors?: string[] } | null;
  const detail = body?.errors?.length ? body.errors[0] : body?.message;
  if (detail) {
    return detail;
  }

  return error.status >= 500
    ? 'Something went wrong on the server. Please try again.'
    : 'The request could not be completed.';
}
