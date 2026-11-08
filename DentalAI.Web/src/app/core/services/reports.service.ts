import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { PracticeReport, ReceivablesReport } from '../models/report.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/** Practice reports for the signed-in clinic. Dates are clinic-local "yyyy-MM-dd". */
@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL)}/reports`;

  practice(from: string, to: string): Observable<PracticeReport> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<ApiResponse<PracticeReport>>(`${this.base}/practice`, { params }).pipe(map((r) => r.data));
  }

  receivables(): Observable<ReceivablesReport> {
    return this.http.get<ApiResponse<ReceivablesReport>>(`${this.base}/receivables`).pipe(map((r) => r.data));
  }
}
