import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { finalize } from 'rxjs';
import { InviteDetails, PASSWORD_HELP, PASSWORD_PATTERN, USER_ROLE_LABELS } from '../../../core/models/auth.models';
import { AuthStateService } from '../../../core/services/auth-state.service';
import { saveErrorText, touchAll } from '../../../shared/forms';

/** /auth/accept-invite?token=... : an invited person sets their own password and is signed in. */
@Component({
  selector: 'app-accept-invite-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, NzAlertModule, NzButtonModule, NzCardModule, NzFormModule, NzInputModule, NzSkeletonModule],
  template: `
    <div class="invite">
      <nz-card class="invite__card" [nzBordered]="false">
        <p class="brand">DentalAI</p>
        @if (loading()) {
          <nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 4 }" />
        } @else if (!invite()) {
          <h1>This link doesn't work</h1>
          <nz-alert nzType="warning" nzShowIcon
                    nzMessage="This invite link is invalid, has already been used, or has expired."
                    nzDescription="Ask your clinic's admin to send you a new link. If you've already set your password, sign in instead." />
          <a nz-button nzType="primary" nzBlock routerLink="/auth/login" class="gap">Go to sign in</a>
        } @else {
          <h1>Welcome, {{ invite()!.firstName }}</h1>
          <p class="lead">
            You've been invited to <strong>{{ invite()!.clinicName }}</strong> as {{ roleLabel() }}.
            Set a password to finish creating your account.
          </p>
          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="submit()">
            <nz-form-item>
              <nz-form-label nzFor="invite-email">Email</nz-form-label>
              <nz-form-control>
                <input nz-input id="invite-email" [value]="invite()!.email" readonly autocomplete="username" />
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzRequired nzFor="invite-password">Password</nz-form-label>
              <nz-form-control [nzErrorTip]="passwordHelp" [nzExtra]="passwordHelp">
                <input nz-input id="invite-password" type="password" formControlName="password" autocomplete="new-password" />
              </nz-form-control>
            </nz-form-item>
            <nz-form-item>
              <nz-form-label nzRequired nzFor="invite-confirm">Confirm password</nz-form-label>
              <nz-form-control nzErrorTip="The passwords don't match.">
                <input nz-input id="invite-confirm" type="password" formControlName="confirm" autocomplete="new-password" />
              </nz-form-control>
            </nz-form-item>
            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap-bottom" /> }
            <button nz-button nzType="primary" nzBlock [nzLoading]="saving()">Set password and sign in</button>
          </form>
        }
      </nz-card>
    </div>
  `,
  styles: [`
    .invite { min-height: 100vh; display: grid; place-items: center; padding: 1rem;
      background: linear-gradient(135deg, #12343b, #1e5660 50%, #f2ede5 50%, #f7f6f2 100%); }
    .invite__card { width: min(460px, 100%); border-radius: 1.5rem; box-shadow: 0 24px 80px rgba(18, 52, 59, 0.18); }
    .brand { text-transform: uppercase; letter-spacing: 0.16em; font-size: 0.72rem; color: var(--da-brand); margin: 0 0 0.5rem; }
    h1 { margin: 0 0 0.5rem; color: #12343b; font-size: 1.5rem; }
    .lead { color: var(--da-ink-2); margin-bottom: 1rem; }
    .gap { margin-top: 1rem; }
    .gap-bottom { margin-bottom: 1rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AcceptInvitePageComponent {
  private readonly auth = inject(AuthStateService);
  private readonly router = inject(Router);
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';

  readonly passwordHelp = PASSWORD_HELP;
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly invite = signal<InviteDetails | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    confirm: ['', Validators.required]
  }, { validators: matchingPasswords });

  constructor() {
    if (!this.token) {
      this.loading.set(false);
      return;
    }
    this.auth.getInvite(this.token).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (invite) => this.invite.set(invite),
      error: () => this.invite.set(null)
    });
  }

  roleLabel(): string {
    const role = this.invite()?.role;
    return role === 'ClinicAdmin' ? 'an admin' : role ? `a ${USER_ROLE_LABELS[role].toLowerCase()}` : 'a team member';
  }

  submit(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      touchAll(this.form);
      if (this.form.errors?.['mismatch']) this.form.controls.confirm.setErrors({ mismatch: true });
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.auth.acceptInvite(this.token, this.form.controls.password.value)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => void this.router.navigate(['/dashboard']),
        error: (error: unknown) => this.error.set(saveErrorText(error))
      });
  }
}

function matchingPasswords(control: AbstractControl): ValidationErrors | null {
  const chosen = control.get('password')?.value;
  const confirm = control.get('confirm');
  if (!confirm?.value) return null;
  const mismatch = chosen !== confirm.value;
  if (mismatch) confirm.setErrors({ ...(confirm.errors ?? {}), mismatch: true });
  else if (confirm.hasError('mismatch')) {
    const { mismatch: _, ...rest } = confirm.errors ?? {};
    confirm.setErrors(Object.keys(rest).length ? rest : null);
  }
  return mismatch ? { mismatch: true } : null;
}
