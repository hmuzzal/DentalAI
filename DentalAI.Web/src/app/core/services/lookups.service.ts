import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, shareReplay, tap } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { CountryOption, TimeZoneOption } from '../models/clinic.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/** Reference lists from /lookups. Each list is fetched once per session (a failed fetch is retried next time). */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  private timeZones$?: Observable<TimeZoneOption[]>;
  private slotIntervals$?: Observable<number[]>;
  private countries$?: Observable<CountryOption[]>;

  /** Main US zones first, then every other zone by current UTC offset. */
  timeZones(): Observable<TimeZoneOption[]> {
    return (this.timeZones$ ??= this.cached<TimeZoneOption[]>('time-zones', () => (this.timeZones$ = undefined)));
  }

  /** Allowed clinic slot intervals, in minutes. */
  slotIntervals(): Observable<number[]> {
    return (this.slotIntervals$ ??= this.cached<number[]>('slot-intervals', () => (this.slotIntervals$ = undefined)));
  }

  /** Countries for the clinic form, United States first. */
  countries(): Observable<CountryOption[]> {
    return (this.countries$ ??= this.cached<CountryOption[]>('countries', () => (this.countries$ = undefined)));
  }

  private cached<T>(path: string, forget: () => void): Observable<T> {
    return this.http.get<ApiResponse<T>>(`${this.apiBaseUrl}/lookups/${path}`).pipe(
      map((response) => response.data),
      tap({ error: forget }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
  }
}
