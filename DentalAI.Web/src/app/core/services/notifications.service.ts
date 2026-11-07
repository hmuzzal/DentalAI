import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse, PagedResult } from '../models/api-response.model';
import {
  MessageChannel, MessageListQuery, NotificationSettings, NotificationSettingsRequest, OutboundMessage
} from '../models/notification.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/** Reminder settings (admins change them) and the clinic's message log (all staff). */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/notifications`;

  settings(): Observable<NotificationSettings> {
    return this.http.get<ApiResponse<NotificationSettings>>(`${this.base}/settings`).pipe(map((r) => r.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  updateSettings(request: NotificationSettingsRequest): Observable<NotificationSettings> {
    return this.http.put<ApiResponse<NotificationSettings>>(`${this.base}/settings`, request, silentErrors()).pipe(map((r) => r.data));
  }

  messages(query: MessageListQuery): Observable<PagedResult<OutboundMessage>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['status', 'channel', 'patientId', 'appointmentId'] as const) {
      const value = query[key];
      if (value) params = params.set(key, value);
    }
    return this.http.get<ApiResponse<PagedResult<OutboundMessage>>>(`${this.base}/messages`, { params }).pipe(map((r) => r.data));
  }

  /** Returns the server's message too: it says whether the message will really be delivered. */
  sendTest(channel: MessageChannel, to: string): Observable<ApiResponse<OutboundMessage>> {
    return this.http.post<ApiResponse<OutboundMessage>>(`${this.base}/test`, { channel, to }, silentErrors());
  }
}
