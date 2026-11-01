import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { SILENT_ERRORS } from '../http/http-context';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { Appointment, CreateAppointmentRequest, RescheduleAppointmentRequest } from '../models/appointment.models';
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

  /** Errors are silent: the booking drawer shows them inline. */
  create(request: CreateAppointmentRequest): Observable<Appointment> {
    return this.http
      .post<ApiResponse<Appointment>>(`${this.apiBaseUrl}/appointments`, request, {
        context: new HttpContext().set(SILENT_ERRORS, true)
      })
      .pipe(map((response) => response.data));
  }

  /** Cancels an appointment. Failures surface through the global error toast. */
  cancel(id: string, notes?: string | null): Observable<Appointment> {
    return this.http
      .post<ApiResponse<Appointment>>(`${this.apiBaseUrl}/appointments/${id}/cancel`, { notes: notes ?? null })
      .pipe(map((response) => response.data));
  }

  /** Moves an appointment to a new start time. Errors are silent: the drawer shows them inline. */
  reschedule(id: string, request: RescheduleAppointmentRequest): Observable<Appointment> {
    return this.http
      .post<ApiResponse<Appointment>>(`${this.apiBaseUrl}/appointments/${id}/reschedule`, request, {
        context: new HttpContext().set(SILENT_ERRORS, true)
      })
      .pipe(map((response) => response.data));
  }
}
