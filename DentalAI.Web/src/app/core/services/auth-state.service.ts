import { HttpClient, HttpContext } from '@angular/common/http';
import { SILENT_ERRORS } from '../http/http-context';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { API_BASE_URL } from '../tokens/api-base-url.token';
import { ApiResponse } from '../models/api-response.model';
import { AuthResponse, LoginRequest, UserProfile, UserRole } from '../models/auth.models';

@Injectable({ providedIn: 'root' })
export class AuthStateService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly accessTokenKey = 'dentalai.access-token';
  private readonly refreshTokenKey = 'dentalai.refresh-token';

  readonly user = signal<UserProfile | null>(null);
  readonly loading = signal(false);
  readonly token = signal<string | null>(localStorage.getItem(this.accessTokenKey));
  readonly isAuthenticated = computed(() => !!this.token());
  readonly role = computed<UserRole | null>(() => this.user()?.role ?? null);

  login(payload: LoginRequest): Observable<AuthResponse> {
    this.loading.set(true);
    return this.http
      // The login form shows its own error, so skip the global error toast.
      .post<ApiResponse<AuthResponse>>(`${this.apiBaseUrl}/auth/login`, payload, {
        context: new HttpContext().set(SILENT_ERRORS, true)
      })
      .pipe(
        map((response) => response.data),
        tap((response) => this.persistSession(response)),
        tap(() => this.loading.set(false))
      );
  }

  loadCurrentUser(): Observable<UserProfile | null> {
    if (!this.token()) {
      this.user.set(null);
      return of(null);
    }

    return this.http.get<ApiResponse<UserProfile>>(`${this.apiBaseUrl}/auth/me`).pipe(
      map((response) => response.data),
      tap((user) => this.user.set(user)),
      catchError(() => {
        this.clearSession(false);
        return of(null);
      })
    );
  }

  logout(redirect = true): void {
    this.clearSession(redirect);
  }

  hasAnyRole(roles: UserRole[]): boolean {
    const currentRole = this.role();
    return currentRole ? roles.includes(currentRole) : false;
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(this.refreshTokenKey);
  }

  private persistSession(response: AuthResponse): void {
    localStorage.setItem(this.accessTokenKey, response.accessToken);
    localStorage.setItem(this.refreshTokenKey, response.refreshToken);
    this.token.set(response.accessToken);
    this.user.set(response.user);
  }

  private clearSession(redirect: boolean): void {
    localStorage.removeItem(this.accessTokenKey);
    localStorage.removeItem(this.refreshTokenKey);
    this.token.set(null);
    this.user.set(null);

    if (redirect) {
      void this.router.navigate(['/auth/login']);
    }
  }
}
