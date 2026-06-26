import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { Appointment } from '../models/appointment.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class AppointmentsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(fromUtc?: string, toUtc?: string): Observable<Appointment[]> {
    let params = new HttpParams();
    if (fromUtc) {
      params = params.set('fromUtc', fromUtc);
    }

    if (toUtc) {
      params = params.set('toUtc', toUtc);
    }

    return this.http
      .get<ApiResponse<Appointment[]>>(`${this.apiBaseUrl}/appointments`, { params })
      .pipe(map((response) => response.data));
  }
}
