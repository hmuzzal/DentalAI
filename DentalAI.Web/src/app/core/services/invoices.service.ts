import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import {
  CreateInvoiceRequest, Invoice, InvoiceDocument, InvoiceDraftRequest, InvoiceList, InvoiceListQuery, RecordPaymentRequest
} from '../models/billing.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/**
 * Invoices and payments. Calls whose errors a form shows inline are silent; the others (issue, delete) rely on the
 * global error toast.
 */
@Injectable({ providedIn: 'root' })
export class InvoicesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/invoices`;

  list(query: InvoiceListQuery): Observable<InvoiceList> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['status', 'patientId', 'appointmentId', 'from', 'to'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    if (query.outstandingOnly) params = params.set('outstandingOnly', true);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    return this.http.get<ApiResponse<InvoiceList>>(this.base, { params }).pipe(map((r) => r.data));
  }

  get(id: string): Observable<Invoice> {
    return this.http.get<ApiResponse<Invoice>>(`${this.base}/${id}`).pipe(map((r) => r.data));
  }

  document(id: string): Observable<InvoiceDocument> {
    return this.http.get<ApiResponse<InvoiceDocument>>(`${this.base}/${id}/document`).pipe(map((r) => r.data));
  }

  create(request: CreateInvoiceRequest): Observable<Invoice> {
    return this.http.post<ApiResponse<Invoice>>(this.base, request, silentErrors()).pipe(map((r) => r.data));
  }

  /** A draft for a completed appointment (409 if it already has an invoice). */
  createFromAppointment(appointmentId: string): Observable<Invoice> {
    return this.http
      .post<ApiResponse<Invoice>>(`${this.base}/from-appointment/${appointmentId}`, {})
      .pipe(map((r) => r.data));
  }

  updateDraft(id: string, request: InvoiceDraftRequest): Observable<Invoice> {
    return this.http.put<ApiResponse<Invoice>>(`${this.base}/${id}`, request, silentErrors()).pipe(map((r) => r.data));
  }

  deleteDraft(id: string): Observable<void> {
    return this.http.delete<ApiResponse<unknown>>(`${this.base}/${id}`).pipe(map(() => undefined));
  }

  issue(id: string): Observable<Invoice> {
    return this.http.post<ApiResponse<Invoice>>(`${this.base}/${id}/issue`, {}).pipe(map((r) => r.data));
  }

  recordPayment(id: string, request: RecordPaymentRequest): Observable<Invoice> {
    return this.http
      .post<ApiResponse<Invoice>>(`${this.base}/${id}/payments`, request, silentErrors())
      .pipe(map((r) => r.data));
  }

  /** Clinic admins only. */
  refund(id: string, paymentId: string, amount: number, reason: string): Observable<Invoice> {
    return this.http
      .post<ApiResponse<Invoice>>(`${this.base}/${id}/payments/${paymentId}/refunds`, { amount, reason }, silentErrors())
      .pipe(map((r) => r.data));
  }

  /** Clinic admins only. */
  void(id: string, reason: string): Observable<Invoice> {
    return this.http
      .post<ApiResponse<Invoice>>(`${this.base}/${id}/void`, { reason }, silentErrors())
      .pipe(map((r) => r.data));
  }
}
