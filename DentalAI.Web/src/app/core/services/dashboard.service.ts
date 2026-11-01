import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { DashboardStats, SchedulingDashboard } from '../models/dashboard.models';
import { ApiResponse } from '../models/api-response.model';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getStats(): Observable<DashboardStats> {
    return this.http
      .get<ApiResponse<DashboardStats>>(`${this.apiBaseUrl}/dashboard/stats`)
      .pipe(map((response) => response.data));
  }

  getScheduling(): Observable<SchedulingDashboard> {
    return this.http
      .get<ApiResponse<SchedulingDashboard>>(`${this.apiBaseUrl}/dashboard/scheduling`)
      .pipe(map((response) => response.data));
  }
}
