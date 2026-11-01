import { computed, inject, Injectable, signal } from '@angular/core';
import { Clinic } from '../models/clinic.models';
import { AuthStateService } from './auth-state.service';
import { ClinicsService } from './clinics.service';

/**
 * The clinic list for super admins, who pick a clinic when adding doctors, patients or services and see a
 * Clinic column in lists. Clinic users never need it (the API uses their own clinic), so it is only fetched
 * for super admins, once, on first use.
 */
@Injectable({ providedIn: 'root' })
export class ClinicOptionsService {
  private readonly auth = inject(AuthStateService);
  private readonly clinicsService = inject(ClinicsService);
  private readonly loaded = signal<Clinic[] | null>(null);
  private requested = false;

  readonly isSuperAdmin = computed(() => this.auth.role() === 'SuperAdmin');

  /** Empty for clinic users, and until the list arrives. */
  readonly clinics = computed(() => {
    if (!this.isSuperAdmin()) return [];
    this.ensureLoaded();
    return this.loaded() ?? [];
  });

  private readonly names = computed(() => new Map(this.clinics().map((c) => [c.id, c.name])));

  clinicName(id: string | null | undefined): string {
    return (id && this.names().get(id)) || '—';
  }

  /** Call after a clinic is added or renamed. */
  refresh(): void {
    this.requested = false;
    this.ensureLoaded();
  }

  private ensureLoaded(): void {
    if (this.requested) return;
    this.requested = true;
    // Deferred so it never writes a signal while a computed is being evaluated.
    queueMicrotask(() =>
      this.clinicsService.getAll().subscribe({
        next: (clinics) => this.loaded.set([...clinics].sort((a, b) => a.name.localeCompare(b.name))),
        error: () => (this.requested = false)
      })
    );
  }
}
