import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { SILENT_ERRORS } from '../http/http-context';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import {
  AvailableSlot,
  CalendarResponse,
  CalendarView,
  DoctorAvailability
} from '../models/scheduling.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class SchedulingService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getCalendar(view: CalendarView, date: string, doctorId?: string, clinicId?: string): Observable<CalendarResponse> {
    let params = new HttpParams().set('date', date);
    if (doctorId) params = params.set('doctorId', doctorId);
    if (clinicId) params = params.set('clinicId', clinicId);
    return this.http.get<ApiResponse<CalendarResponse>>(`${this.apiBaseUrl}/calendar/${view}`, { params })
      .pipe(map((response) => response.data));
  }

  /** Errors are silent: the slot picker shows its own error state. */
  getAvailableSlots(doctorId: string, date: string, serviceId: string, excludeAppointmentId?: string | null): Observable<AvailableSlot[]> {
    let params = new HttpParams().set('doctorId', doctorId).set('date', date).set('serviceId', serviceId);
    // When rescheduling, the appointment being moved must not block its own neighbouring slots.
    if (excludeAppointmentId) params = params.set('excludeAppointmentId', excludeAppointmentId);
    const context = new HttpContext().set(SILENT_ERRORS, true);
    return this.http.get<ApiResponse<AvailableSlot[]>>(`${this.apiBaseUrl}/appointments/available-slots`, { params, context })
      .pipe(map((response) => response.data));
  }

  getAvailability(doctorId: string): Observable<DoctorAvailability> {
    return this.http.get<ApiResponse<DoctorAvailability>>(`${this.apiBaseUrl}/doctorSchedules/${doctorId}`)
      .pipe(map((response) => response.data));
  }

  saveAvailability(doctorId: string, availability: Omit<DoctorAvailability, 'doctorId'>): Observable<DoctorAvailability> {
    return this.http.put<ApiResponse<DoctorAvailability>>(`${this.apiBaseUrl}/doctorSchedules/${doctorId}`, availability)
      .pipe(map((response) => response.data));
  }
}
