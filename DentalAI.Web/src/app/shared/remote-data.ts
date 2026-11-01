import { DestroyRef, inject, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

export type LoadStatus = 'loading' | 'ready' | 'error';

export interface RemoteData<T> {
  readonly value: () => T;
  readonly status: () => LoadStatus;
  /** Re-runs the request, e.g. from a "Try again" button. */
  reload(): void;
}

/**
 * Loads data once and tracks loading / ready / error, so pages can show proper states.
 * Must be called in an injection context (a field initialiser or constructor).
 *
 *   readonly doctors = remoteData(() => this.doctorsService.getAll(), []);
 */
export function remoteData<T>(fetch: () => Observable<T>, initial: T): RemoteData<T> {
  const value = signal<T>(initial);
  const status = signal<LoadStatus>('loading');
  let subscription: Subscription | undefined;

  const reload = () => {
    subscription?.unsubscribe();
    status.set('loading');
    subscription = fetch().subscribe({
      next: (result) => {
        value.set(result);
        status.set('ready');
      },
      error: () => status.set('error')
    });
  };

  inject(DestroyRef).onDestroy(() => subscription?.unsubscribe());
  reload();

  return { value: value.asReadonly(), status: status.asReadonly(), reload };
}
