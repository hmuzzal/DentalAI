import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { silentErrors } from '../../shared/forms';
import { ApiResponse } from '../models/api-response.model';
import { InviteStaffRequest, StaffInvite, StaffMember, StaffRole } from '../models/staff.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

/**
 * Staff accounts. With no clinicId: the signed-in clinic admin's own clinic (/staff). With a clinicId: a platform
 * admin managing that clinic (/clinics/{id}/staff), who can invite admins and deactivate or reactivate accounts.
 */
@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  list(clinicId?: string): Observable<StaffMember[]> {
    return this.http.get<ApiResponse<StaffMember[]>>(this.base(clinicId)).pipe(map((r) => r.data));
  }

  /** Errors are left to the caller (the form shows them inline). */
  invite(request: InviteStaffRequest, clinicId?: string): Observable<StaffInvite> {
    return this.http
      .post<ApiResponse<StaffInvite>>(`${this.base(clinicId)}/invite`, request, silentErrors())
      .pipe(map((r) => r.data));
  }

  /** A new link; the previous one stops working. */
  renewInvite(userId: string, clinicId?: string): Observable<StaffInvite> {
    return this.http
      .post<ApiResponse<StaffInvite>>(`${this.base(clinicId)}/${userId}/renew-invite`, {})
      .pipe(map((r) => r.data));
  }

  /** Clinic admins only. */
  changeRole(userId: string, role: StaffRole): Observable<StaffMember> {
    return this.http
      .put<ApiResponse<StaffMember>>(`${this.base()}/${userId}/role`, { role })
      .pipe(map((r) => r.data));
  }

  setActive(userId: string, active: boolean, clinicId?: string): Observable<StaffMember> {
    return this.http
      .post<ApiResponse<StaffMember>>(`${this.base(clinicId)}/${userId}/${active ? 'reactivate' : 'deactivate'}`, {})
      .pipe(map((r) => r.data));
  }

  private base(clinicId?: string): string {
    return clinicId ? `${this.apiBaseUrl}/clinics/${clinicId}/staff` : `${this.apiBaseUrl}/staff`;
  }
}
