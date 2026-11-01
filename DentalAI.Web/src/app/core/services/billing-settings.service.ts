import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal, untracked } from '@angular/core';
import { map, Observable, shareReplay, tap } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import { BillingSettings, BillingSettingsRequest, CurrencyOption } from '../models/billing.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';
import { AuthStateService } from './auth-state.service';

/**
 * The clinic's billing settings, loaded once and shared, so every price on screen uses the clinic's currency.
 * Clinic staff only (platform admins get 403 and never call it).
 */
@Injectable({ providedIn: 'root' })
export class BillingSettingsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);
  private readonly settingsSignal = signal<BillingSettings | null>(null);
  private requested = false;
  private currencies$?: Observable<CurrencyOption[]>;
  private readonly auth = inject(AuthStateService);
  /** The clinic the cached settings belong to. */
  private loadedFor: string | null = null;

  /** Null until loaded. Reloads when a different clinic's user signs in on this browser. */
  readonly settings = computed(() => {
    const clinicId = this.auth.user()?.clinicId ?? null;
    if (clinicId !== this.loadedFor) {
      untracked(() => this.reset(clinicId));
    }
    this.ensureLoaded();
    return this.settingsSignal();
  });

  /** The clinic's currency (USD until the settings arrive). */
  readonly currency = computed(() => this.settings()?.currency ?? 'USD');

  load(): Observable<BillingSettings> {
    return this.http.get<ApiResponse<BillingSettings>>(`${this.apiBaseUrl}/billing/settings`).pipe(
      map((r) => r.data),
      tap((settings) => this.settingsSignal.set(settings))
    );
  }

  /** Clinic admins only. Errors are left to the caller (the form shows them inline). */
  update(request: BillingSettingsRequest): Observable<BillingSettings> {
    return this.http.put<ApiResponse<BillingSettings>>(`${this.apiBaseUrl}/billing/settings`, request, silentErrors()).pipe(
      map((r) => r.data),
      tap((settings) => this.settingsSignal.set(settings))
    );
  }

  currencies(): Observable<CurrencyOption[]> {
    return (this.currencies$ ??= this.http
      .get<ApiResponse<CurrencyOption[]>>(`${this.apiBaseUrl}/billing/currencies`)
      .pipe(map((r) => r.data), shareReplay({ bufferSize: 1, refCount: false })));
  }

  /** Forget the cached settings (another clinic signed in). Deferred: never write signals inside a computed. */
  private reset(clinicId: string | null): void {
    this.loadedFor = clinicId;
    this.requested = !clinicId; // nothing to load without a clinic (signed out, or a platform admin)
    queueMicrotask(() => this.settingsSignal.set(null));
  }

  private ensureLoaded(): void {
    if (this.requested) return;
    this.requested = true;
    // Deferred so it never writes a signal while a computed is being evaluated.
    queueMicrotask(() => this.load().subscribe({ error: () => (this.requested = false) }));
  }
}
