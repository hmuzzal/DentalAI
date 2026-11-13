import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzResultModule } from 'ng-zorro-antd/result';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalApiService } from '../../core/services/portal.service';
import { PhonePipe } from '../../shared/phone.pipe';
import { PortalContext } from './portal-context';

/** The portal's frame: clinic name, the patient's menu, and the clinic's contact details. Mobile-first. */
@Component({
  selector: 'app-portal-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NzButtonModule, NzIconModule, NzResultModule, NzSkeletonModule, PhonePipe],
  template: `
    <div class="portal">
      <header class="portal__header">
        <div class="portal__bar">
          <div class="portal__brand">
            <span class="portal__mark" aria-hidden="true">{{ initial() }}</span>
            <span>
              <strong>{{ context.clinic()?.name ?? 'Online booking' }}</strong>
              <small>Online booking</small>
            </span>
          </div>
          @if (signedIn()) {
            <button nz-button nzType="text" class="portal__signout" (click)="session.signOut()">
              <span nz-icon nzType="logout"></span><span class="hide-sm">Sign out</span>
            </button>
          }
        </div>
        @if (signedIn()) {
          <nav class="portal__nav" aria-label="Portal">
            <a [routerLink]="context.link()" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
              <span nz-icon nzType="calendar"></span>Visits</a>
            <a [routerLink]="context.link('book')" routerLinkActive="active"><span nz-icon nzType="plus"></span>Book</a>
            @if (context.assistant()) {
              <a [routerLink]="context.link('assistant')" routerLinkActive="active"><span nz-icon nzType="message"></span>Ask us</a>
            }
            <a [routerLink]="context.link('invoices')" routerLinkActive="active"><span nz-icon nzType="file-text"></span>Invoices</a>
            <a [routerLink]="context.link('profile')" routerLinkActive="active"><span nz-icon nzType="user"></span>My details</a>
          </nav>
        }
      </header>

      <main class="portal__main">
        @switch (context.status()) {
          @case ('loading') { <nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 5 }" /> }
          @case ('missing') {
            <nz-result nzStatus="404" nzTitle="Online booking isn't available"
                       nzSubTitle="This link may be wrong, or the clinic has turned online booking off. Please contact the clinic." />
          }
          @default { <router-outlet /> }
        }
      </main>

      @if (context.clinic(); as clinic) {
        <footer class="portal__footer">
          <strong>{{ clinic.name }}</strong>
          @if (clinic.phone) { <a [href]="'tel:' + clinic.phone">{{ clinic.phone | phone }}</a> }
          @if (clinic.address) { <span>{{ clinic.address }}</span> }
        </footer>
      }
    </div>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: var(--da-bg); }
    .portal { display: flex; flex-direction: column; min-height: 100vh; }
    .portal__header { background: var(--da-surface); border-bottom: 1px solid var(--da-border); position: sticky; top: 0; z-index: 10; }
    .portal__bar, .portal__nav, .portal__main, .portal__footer { width: 100%; max-width: 760px; margin: 0 auto; padding-inline: var(--da-space-4); }
    .portal__bar { display: flex; align-items: center; justify-content: space-between; gap: var(--da-space-3); min-height: 60px; }
    .portal__brand { display: flex; align-items: center; gap: var(--da-space-3); min-width: 0; }
    .portal__brand strong { display: block; font-size: 16px; line-height: 1.2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .portal__brand small { color: var(--da-ink-3); font-size: 12px; }
    .portal__mark { flex: none; display: grid; place-items: center; width: 36px; height: 36px; border-radius: var(--da-radius);
      background: var(--da-brand-soft); color: var(--da-brand-deep); font-weight: 700; }
    .portal__nav { display: flex; gap: var(--da-space-1); overflow-x: auto; padding-bottom: var(--da-space-2); }
    .portal__nav a { display: inline-flex; align-items: center; gap: 6px; padding: 6px 12px; border-radius: 999px; color: var(--da-ink-2);
      white-space: nowrap; font-weight: 500; }
    .portal__nav a:hover { color: var(--da-ink); background: var(--da-surface-2); }
    .portal__nav a.active { color: var(--da-brand-deep); background: var(--da-brand-soft); }
    .portal__main { flex: 1; padding-block: var(--da-space-5); }
    .portal__footer { display: flex; flex-wrap: wrap; gap: var(--da-space-1) var(--da-space-3); padding-block: var(--da-space-4);
      color: var(--da-ink-3); font-size: 13px; border-top: 1px solid var(--da-border); }
    .portal__footer strong { color: var(--da-ink-2); }
    @media (max-width: 480px) { .hide-sm { display: none; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalShellComponent {
  readonly context = inject(PortalContext);
  readonly session = inject(PortalSessionService);
  private readonly api = inject(PortalApiService);

  readonly signedIn = () => this.session.isSignedInTo(this.context.slug());
  readonly initial = () => (this.context.clinic()?.name ?? 'D').trim().charAt(0).toUpperCase();

  constructor() {
    // The assistant link shows only when the server has a model configured.
    effect(() => {
      const signedIn = this.signedIn();
      untracked(() => {
        if (!signedIn) {
          this.context.assistant.set(false);
          return;
        }
        this.api.assistantStatus().pipe(catchError(() => of(null)))
          .subscribe((status) => {
            this.context.assistant.set(status?.enabled === true);
            if (status?.maxMessageLength) this.context.assistantMaxLength.set(status.maxMessageLength);
          });
      });
    });

    inject(ActivatedRoute).paramMap.pipe(
      map((params) => (params.get('slug') ?? '').toLowerCase()),
      tap((slug) => {
        this.context.slug.set(slug);
        this.context.status.set('loading');
      }),
      switchMap((slug) => this.api.clinic(slug).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(inject(DestroyRef))
    ).subscribe((clinic) => {
      this.context.clinic.set(clinic);
      this.context.status.set(clinic ? 'ready' : 'missing');
      // A portal that is gone (or switched off) ends the stored session for it.
      if (clinic && this.session.isSignedInTo(clinic.slug)) this.session.updateClinic(clinic);
      if (!clinic && this.session.isSignedInTo(this.context.slug())) this.session.end(null);
    });
  }
}
