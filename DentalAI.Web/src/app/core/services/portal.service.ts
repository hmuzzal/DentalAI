import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import {
  PortalAppointment, PortalAssistantReply, PortalAssistantStatus, PortalClinic, PortalDoctor, PortalInvoice, PortalInvoiceList, PortalProfile, PortalProfileRequest,
  PortalRegisterRequest, PortalService as PortalServiceItem, PortalSession, PortalSettings, PortalSettingsRequest,
  PortalStartResponse, PortalVerifyResponse
} from '../models/portal.models';
import { AvailableSlot } from '../models/scheduling.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/** Patient portal API: sign-in (anonymous) and the signed-in patient's own records. */
@Injectable({ providedIn: 'root' })
export class PortalApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/portal`;

  // ---- Sign-in (callers show their own errors) ----

  clinic(slug: string): Observable<PortalClinic> {
    return this.http.get<ApiResponse<PortalClinic>>(`${this.base}/clinics/${encodeURIComponent(slug)}`, silentErrors())
      .pipe(map((r) => r.data));
  }

  start(slug: string, contact: string): Observable<ApiResponse<PortalStartResponse>> {
    return this.http.post<ApiResponse<PortalStartResponse>>(`${this.auth(slug)}/start`, { contact }, silentErrors());
  }

  verify(slug: string, contact: string, code: string): Observable<PortalVerifyResponse> {
    return this.http.post<ApiResponse<PortalVerifyResponse>>(`${this.auth(slug)}/verify`, { contact, code }, silentErrors())
      .pipe(map((r) => r.data));
  }

  select(slug: string, ticket: string, patientId: string): Observable<PortalSession> {
    return this.http.post<ApiResponse<PortalSession>>(`${this.auth(slug)}/select`, { ticket, patientId }, silentErrors())
      .pipe(map((r) => r.data));
  }

  register(slug: string, request: PortalRegisterRequest): Observable<PortalSession> {
    return this.http.post<ApiResponse<PortalSession>>(`${this.auth(slug)}/register`, request, silentErrors())
      .pipe(map((r) => r.data));
  }

  // ---- Signed-in patient ----

  profile(): Observable<PortalProfile> {
    return this.http.get<ApiResponse<PortalProfile>>(`${this.base}/me`).pipe(map((r) => r.data));
  }

  updateProfile(request: PortalProfileRequest): Observable<PortalProfile> {
    return this.http.put<ApiResponse<PortalProfile>>(`${this.base}/me`, request, silentErrors()).pipe(map((r) => r.data));
  }

  services(): Observable<PortalServiceItem[]> {
    return this.http.get<ApiResponse<PortalServiceItem[]>>(`${this.base}/services`).pipe(map((r) => r.data));
  }

  doctors(): Observable<PortalDoctor[]> {
    return this.http.get<ApiResponse<PortalDoctor[]>>(`${this.base}/doctors`).pipe(map((r) => r.data));
  }

  /** Same shape as the staff slots, so the shared time-slot selector can show them. */
  slots(doctorId: string, date: string, serviceId: string, rescheduleAppointmentId?: string | null): Observable<AvailableSlot[]> {
    let params = new HttpParams().set('doctorId', doctorId).set('serviceId', serviceId).set('date', date);
    if (rescheduleAppointmentId) params = params.set('rescheduleAppointmentId', rescheduleAppointmentId);
    return this.http.get<ApiResponse<AvailableSlot[]>>(`${this.base}/slots`, { params }).pipe(map((r) => r.data));
  }

  appointments(): Observable<PortalAppointment[]> {
    return this.http.get<ApiResponse<PortalAppointment[]>>(`${this.base}/appointments`).pipe(map((r) => r.data));
  }

  /** Returns the server's message too ("Request sent..." or "You're booked."). */
  book(request: { doctorId: string; serviceId: string; startTime: string; note: string | null }): Observable<ApiResponse<PortalAppointment>> {
    return this.http.post<ApiResponse<PortalAppointment>>(`${this.base}/appointments`, request, silentErrors());
  }

  reschedule(id: string, startTime: string): Observable<ApiResponse<PortalAppointment>> {
    return this.http.post<ApiResponse<PortalAppointment>>(`${this.base}/appointments/${id}/reschedule`, { startTime }, silentErrors());
  }

  cancel(id: string): Observable<PortalAppointment> {
    return this.http.post<ApiResponse<PortalAppointment>>(`${this.base}/appointments/${id}/cancel`, {}).pipe(map((r) => r.data));
  }

  invoices(page = 1, pageSize = 20): Observable<PortalInvoiceList> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<ApiResponse<PortalInvoiceList>>(`${this.base}/invoices`, { params }).pipe(map((r) => r.data));
  }

  invoice(id: string): Observable<PortalInvoice> {
    return this.http.get<ApiResponse<PortalInvoice>>(`${this.base}/invoices/${id}`).pipe(map((r) => r.data));
  }

  // ---- AI assistant (callers show their own errors) ----

  assistantStatus(): Observable<PortalAssistantStatus> {
    return this.http.get<ApiResponse<PortalAssistantStatus>>(`${this.base}/assistant`, silentErrors()).pipe(map((r) => r.data));
  }

  sendToAssistant(conversationId: string | null, message: string): Observable<PortalAssistantReply> {
    return this.http.post<ApiResponse<PortalAssistantReply>>(`${this.base}/assistant/messages`, { conversationId, message }, silentErrors())
      .pipe(map((r) => r.data));
  }

  private auth(slug: string): string {
    return `${this.base}/clinics/${encodeURIComponent(slug)}/auth`;
  }
}

/** Staff side: the clinic's portal settings (admins). */
@Injectable({ providedIn: 'root' })
export class PortalSettingsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/portalSettings`;

  get(): Observable<PortalSettings> {
    return this.http.get<ApiResponse<PortalSettings>>(this.base).pipe(map((r) => r.data));
  }

  update(request: PortalSettingsRequest): Observable<PortalSettings> {
    return this.http.put<ApiResponse<PortalSettings>>(this.base, request, silentErrors()).pipe(map((r) => r.data));
  }
}
