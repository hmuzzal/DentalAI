import { HttpErrorResponse, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthStateService } from '../services/auth-state.service';
import { isPortalAuthRequest, isPortalRequest, PortalSessionService } from '../services/portal-session.service';

/**
 * Staff requests carry the staff token; patient portal requests carry the patient token (never the staff one).
 * A portal request that gets 401 refreshes the patient session once and is retried.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (isPortalRequest(req.url)) {
    return portalRequest(req, next, inject(PortalSessionService));
  }

  const token = inject(AuthStateService).token();
  return next(token ? withBearer(req, token) : req);
};

function portalRequest(req: HttpRequest<unknown>, next: HttpHandlerFn, portal: PortalSessionService) {
  if (isPortalAuthRequest(req.url)) {
    return next(req);
  }

  const token = portal.token();
  return next(token ? withBearer(req, token) : req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !portal.session()) {
        return throwError(() => error);
      }
      return portal.refresh().pipe(
        switchMap((renewed) => {
          const fresh = portal.token();
          return renewed && fresh ? next(withBearer(req, fresh)) : throwError(() => error);
        })
      );
    })
  );
}

function withBearer(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}
