import { HttpClient, HttpContext } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, map, Observable, of, shareReplay } from 'rxjs';
import { SILENT_ERRORS } from '../http/http-context';
import { ApiResponse } from '../models/api-response.model';
import { PortalClinic, PortalSession } from '../models/portal.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

const STORAGE_KEY = 'dentalai.portal-session';

/**
 * The signed-in patient on the clinic portal. Kept apart from the staff session (own storage key, own tokens),
 * so a patient and a staff member can use the same browser without affecting each other.
 */
@Injectable({ providedIn: 'root' })
export class PortalSessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = `${inject(API_BASE_URL)}/portal`;

  readonly session = signal<PortalSession | null>(readStored());
  readonly token = computed(() => this.session()?.accessToken ?? null);
  readonly patient = computed(() => this.session()?.patient ?? null);
  readonly clinic = computed<PortalClinic | null>(() => this.session()?.clinic ?? null);

  private refreshing: Observable<boolean> | null = null;

  /** True when signed in to this clinic's portal. */
  isSignedInTo(slug: string): boolean {
    return this.session()?.clinic.slug === slug.toLowerCase();
  }

  start(session: PortalSession): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    this.session.set(session);
  }

  /** Clinic info may change (rules, phone); keep the stored copy fresh. */
  updateClinic(clinic: PortalClinic): void {
    const current = this.session();
    if (current) this.start({ ...current, clinic });
  }

  /**
   * Swaps the refresh token for a new access token. Concurrent callers share one request.
   * Emits false (and signs out) when the session can't be renewed.
   */
  refresh(): Observable<boolean> {
    const current = this.session();
    if (!current) return of(false);
    if (this.refreshing) return this.refreshing;

    this.refreshing = this.http
      .post<ApiResponse<PortalSession>>(`${this.base}/auth/refresh`, { refreshToken: current.refreshToken }, {
        context: new HttpContext().set(SILENT_ERRORS, true)
      })
      .pipe(
        map((response) => {
          this.start(response.data);
          return true;
        }),
        catchError(() => {
          this.end(current.clinic.slug);
          return of(false);
        }),
        finalize(() => (this.refreshing = null)),
        shareReplay(1)
      );
    return this.refreshing;
  }

  /** Revokes the refresh token on the server (best effort) and returns to the clinic's sign-in page. */
  signOut(): void {
    const current = this.session();
    if (current) {
      this.http
        .post(`${this.base}/auth/logout`, { refreshToken: current.refreshToken }, { context: new HttpContext().set(SILENT_ERRORS, true) })
        .subscribe({ error: () => undefined });
    }
    this.end(current?.clinic.slug ?? null);
  }

  /** Forgets the session locally and goes to the sign-in page. */
  end(slug: string | null): void {
    localStorage.removeItem(STORAGE_KEY);
    this.session.set(null);
    if (slug) void this.router.navigate(['/p', slug, 'sign-in']);
  }
}

function readStored(): PortalSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as PortalSession) : null;
  } catch {
    return null;
  }
}

/** Requests that carry the patient token (and never the staff one). */
export function isPortalRequest(url: string): boolean {
  return url.includes('/api/portal/');
}

/** Sign-in calls: anonymous, and a 401 from them is an answer, not an expired session. */
export function isPortalAuthRequest(url: string): boolean {
  return /\/api\/portal\/(auth\/|clinics\/)/.test(url);
}

