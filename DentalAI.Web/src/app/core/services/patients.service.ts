import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse, PagedResult } from '../models/api-response.model';
import { Patient, PatientRequest } from '../models/patient.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

export interface PatientSearch {
  search?: string;
  /** 1-based */
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class PatientsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(): Observable<Patient[]> {
    return this.http
      .get<ApiResponse<Patient[]>>(`${this.apiBaseUrl}/patients`)
      .pipe(map((response) => response.data));
  }

  /** Server-side search (name, phone or email) and paging. */
  search(query: PatientSearch): Observable<PagedResult<Patient>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    return this.http
      .get<ApiResponse<PagedResult<Patient>>>(`${this.apiBaseUrl}/patients/search`, { params })
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  create(request: PatientRequest): Observable<Patient> {
    return this.http
      .post<ApiResponse<Patient>>(`${this.apiBaseUrl}/patients`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  update(id: string, request: PatientRequest): Observable<Patient> {
    return this.http
      .put<ApiResponse<Patient>>(`${this.apiBaseUrl}/patients/${id}`, request, silentErrors())
      .pipe(map((response) => response.data));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiBaseUrl}/patients/${id}`).pipe(map(() => undefined));
  }
}
