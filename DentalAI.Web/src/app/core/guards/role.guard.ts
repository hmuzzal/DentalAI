import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { UserRole } from '../models/auth.models';
import { AuthStateService } from '../services/auth-state.service';

/**
 * Lets the route through only for the roles in `data.roles`. On a page refresh or a direct link the profile
 * hasn't loaded yet, so it is loaded first (otherwise every role-restricted page bounced to the dashboard).
 */
export const roleGuard: CanActivateFn = (route) => {
  const authState = inject(AuthStateService);
  const router = inject(Router);
  const allowedRoles = (route.data?.['roles'] as UserRole[] | undefined) ?? [];
  const decide = () => allowedRoles.length === 0 || authState.hasAnyRole(allowedRoles)
    ? true
    : router.createUrlTree(['/dashboard']);

  if (authState.user() || !authState.isAuthenticated()) {
    return decide();
  }

  return authState.loadCurrentUser().pipe(map(() => decide()));
};
