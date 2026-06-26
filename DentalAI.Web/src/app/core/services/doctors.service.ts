import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { Doctor } from '../models/doctor.models';
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
}
