import { computed, inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PortalClinic } from '../../core/models/portal.models';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { effectiveTimeZone, formatInZone } from '../../core/time-zone';

/** The clinic whose portal is open (from /p/:slug), shared by the portal pages. */
@Injectable({ providedIn: 'root' })
export class PortalContext {
  readonly slug = signal('');
  readonly clinic = signal<PortalClinic | null>(null);
  readonly status = signal<'loading' | 'ready' | 'missing'>('loading');
  /** Whether the signed-in patient can chat with the AI assistant (a server setting). */
  readonly assistant = signal(false);
  /** Longest message the assistant takes (a server setting). */
  readonly assistantMaxLength = signal(1000);
  /** Every time in the portal is shown in the clinic's zone. */
  readonly zone = computed(() => effectiveTimeZone(this.clinic()?.timeZone));

  /** "Tue, Nov 10 · 9:00 AM" in the clinic's zone. */
  when(utc: string): string {
    const zone = this.zone();
    return `${formatInZone(utc, zone, { weekday: 'short', month: 'short', day: 'numeric' })} · ${formatInZone(utc, zone, { hour: 'numeric', minute: '2-digit' })}`;
  }

  /** Links inside the current clinic's portal. */
  link(...parts: string[]): string[] {
    return ['/p', this.slug(), ...parts];
  }
}

/** Portal pages need a patient signed in to this clinic's portal; otherwise go to its sign-in page. */
export const portalGuard: CanActivateFn = (route) => {
  const slug = route.pathFromRoot.map((r) => r.params['slug']).find(Boolean) as string | undefined;
  const session = inject(PortalSessionService);
  if (slug && session.isSignedInTo(slug)) return true;
  return inject(Router).createUrlTree(['/p', slug ?? '', 'sign-in']);
};
