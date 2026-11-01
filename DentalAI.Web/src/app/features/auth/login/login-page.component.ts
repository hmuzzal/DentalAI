import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, isDevMode, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { finalize } from 'rxjs';
import { AuthStateService } from '../../../core/services/auth-state.service';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzFormModule, NzIconModule, NzInputModule],
  template: `
    <div class="login">
      <section class="login__hero">
        <p>DentalAI</p>
        <h1>Scheduling, patients and reminders for your dental clinic.</h1>
        @if (devMode) {
          <span>Development seed login: superadmin&#64;dentalai.local / SuperAdmin123!</span>
        }
      </section>

      <nz-card class="login__card" [nzBordered]="false">
        <form nz-form [formGroup]="form" (ngSubmit)="submit()">
          <h2>Sign in</h2>
          @if (error()) {
            <nz-alert nzType="error" [nzMessage]="error()!" class="login__alert"></nz-alert>
          }

          <nz-form-item>
            <nz-form-control>
              <input nz-input formControlName="email" placeholder="Email address" />
            </nz-form-control>
          </nz-form-item>

          <nz-form-item>
            <nz-form-control>
              <input nz-input type="password" formControlName="password" placeholder="Password" />
            </nz-form-control>
          </nz-form-item>

          <button nz-button nzType="primary" nzBlock [disabled]="form.invalid || authState.loading()">
            {{ authState.loading() ? 'Signing in...' : 'Sign in' }}
          </button>
        </form>
      </nz-card>
    </div>
  `,
  styles: [`
    .login { min-height: 100vh; display: grid; grid-template-columns: 1.2fr 0.8fr; background:
      radial-gradient(circle at top left, rgba(232, 173, 83, 0.22), transparent 30%),
      linear-gradient(135deg, #12343b, #1e5660 52%, #f2ede5 52%, #f7f6f2 100%); }
    .login__hero { padding: 4rem; color: #fff; display: flex; flex-direction: column; justify-content: center; }
    .login__hero p { text-transform: uppercase; letter-spacing: 0.16em; font-size: 0.72rem; opacity: 0.75; }
    .login__hero h1 { font-size: clamp(2.6rem, 4vw, 4.25rem); line-height: 1.05; margin: 0.75rem 0 1rem; max-width: 16ch; color: #fff; }
    .login__hero span { color: rgba(255,255,255,0.8); max-width: 24rem; }
    .login__card { align-self: center; justify-self: center; width: min(420px, calc(100% - 2rem)); border-radius: 1.5rem; box-shadow: 0 24px 80px rgba(18, 52, 59, 0.18); }
    form { display: grid; gap: 1rem; }
    h2 { margin: 0 0 0.5rem; color: #12343b; }
    .login__alert { margin-bottom: 0.25rem; }
    @media (max-width: 900px) {
      .login { grid-template-columns: 1fr; padding: 1rem; }
      .login__hero { padding: 2rem 1rem 0; }
      .login__card { margin: 1rem 0 2rem; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  readonly authState = inject(AuthStateService);
  readonly error = signal<string | null>(null);
  readonly devMode = isDevMode();

  // Pre-fill the seed login in development builds only.
  readonly form = this.fb.nonNullable.group({
    email: [this.devMode ? 'superadmin@dentalai.local' : '', [Validators.required, Validators.email]],
    password: [this.devMode ? 'SuperAdmin123!' : '', Validators.required]
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.error.set(null);
    this.authState
      .login(this.form.getRawValue())
      .pipe(finalize(() => this.authState.loading.set(false)))
      .subscribe({
        next: () => void this.router.navigate(['/dashboard']),
        error: (error) => {
          this.error.set(error?.error?.message ?? 'Unable to sign in.');
        }
      });
  }
}
