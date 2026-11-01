import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import { Doctor, DoctorRequest } from '../models/doctor.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class DoctorsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(): Observable<Doctor[]> {
    return this.http
      .get<ApiResponse<Doctor[]>>(`${this.apiBaseUrl}/doctors`)
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  create(request: DoctorRequest): Observable<Doctor> {
    return this.http
      .post<ApiResponse<Doctor>>(`${this.apiBaseUrl}/doctors`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  update(id: string, request: DoctorRequest): Observable<Doctor> {
    return this.http
      .put<ApiResponse<Doctor>>(`${this.apiBaseUrl}/doctors/${id}`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  /** 409 when the doctor still has upcoming appointments; the global error toast explains why. */
  delete(id: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiBaseUrl}/doctors/${id}`).pipe(map(() => undefined));
  }
}
