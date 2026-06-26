import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { DentalService } from '../models/service.models';
import { API_BASE_URL } from '../tokens/api-base-url.token';

@Injectable({ providedIn: 'root' })
export class ServicesCatalogService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL);

  getAll(): Observable<DentalService[]> {
    return this.http
      .get<ApiResponse<DentalService[]>>(`${this.apiBaseUrl}/services`)
      .pipe(map((response) => response.data));
  }
}
