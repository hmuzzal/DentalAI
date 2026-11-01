import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { PlatformOverview } from '../models/platform.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/** Platform admins only. */
@Injectable({ providedIn: 'root' })
export class PlatformService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getOverview(): Observable<PlatformOverview> {
    return this.http
      .get<ApiResponse<PlatformOverview>>(`${this.apiBaseUrl}/platform/overview`)
      .pipe(map((response) => response.data));
  }
}
