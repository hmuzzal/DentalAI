import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import { DentalService, ServiceRequest } from '../models/service.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class ServicesCatalogService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(): Observable<DentalService[]> {
    return this.http
      .get<ApiResponse<DentalService[]>>(`${this.apiBaseUrl}/services`)
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  create(request: ServiceRequest): Observable<DentalService> {
    return this.http
      .post<ApiResponse<DentalService>>(`${this.apiBaseUrl}/services`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  update(id: string, request: ServiceRequest): Observable<DentalService> {
    return this.http
      .put<ApiResponse<DentalService>>(`${this.apiBaseUrl}/services/${id}`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiBaseUrl}/services/${id}`).pipe(map(() => undefined));
  }
}
