import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { Clinic, ClinicRequest } from '../models/clinic.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class ClinicsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(): Observable<Clinic[]> {
    return this.http
      .get<ApiResponse<Clinic[]>>(`${this.apiBaseUrl}/clinics`)
      .pipe(map((response) => response.data));
  }

  create(request: ClinicRequest): Observable<Clinic> {
    return this.http
      .post<ApiResponse<Clinic>>(`${this.apiBaseUrl}/clinics`, request)
      .pipe(map((response) => response.data));
  }

  update(id: string, request: ClinicRequest): Observable<Clinic> {
    return this.http
      .put<ApiResponse<Clinic>>(`${this.apiBaseUrl}/clinics/${id}`, request)
      .pipe(map((response) => response.data));
  }
}
