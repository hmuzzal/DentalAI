import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { finalize, Observable } from 'rxjs';
import { PortalSession, PortalVerifyResponse } from '../../core/models/portal.models';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalApiService } from '../../core/services/portal.service';
import { optional, PHONE_PATTERN, saveErrorText, touchAll } from '../../shared/forms';
import { formatPhone } from '../../shared/phone.pipe';
import { PortalContext } from './portal-context';

type Step = 'contact' | 'code' | 'choose' | 'register';

/**
 * /p/:slug/sign-in. No passwords: the patient types an email or mobile number, gets a 6-digit code, then picks
 * who they are (one phone can be shared by a family) or registers as a new patient.
 */
@Component({
  selector: 'app-portal-sign-in',
  standalone: true,
  imports: [
    FormsModule, ReactiveFormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzCheckboxModule, NzFormModule, NzIconModule,
    NzInputModule
  ],
  template: `
    <nz-card class="card">
      @switch (step()) {
        @case ('contact') {
          <h1>Sign in to book</h1>
          <p class="lead">Enter your email address or mobile number. We'll send you a code to sign in — no password needed.</p>
          <form (ngSubmit)="sendCode()">
            <label class="label" for="contact">Email or mobile number</label>
            <input nz-input nzSize="large" id="contact" name="contact" autocomplete="username" [(ngModel)]="contact"
                   placeholder="you@example.com or (212) 555-0142" [disabled]="busy()" />
            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <button nz-button nzType="primary" nzSize="large" nzBlock class="gap" [nzLoading]="busy()" [disabled]="!contact().trim()">
              Send code
            </button>
          </form>
        }

        @case ('code') {
          <button nz-button nzType="link" class="back" (click)="restart()"><span nz-icon nzType="left"></span>Use a different email or phone</button>
          <h1>Enter your code</h1>
          <p class="lead">{{ sentText() }}</p>
          <form (ngSubmit)="verify()">
            <label class="label" for="code">6-digit code</label>
            <input nz-input nzSize="large" id="code" name="code" class="code" inputmode="numeric" autocomplete="one-time-code"
                   maxlength="6" [(ngModel)]="code" placeholder="123456" [disabled]="busy()" />
            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <button nz-button nzType="primary" nzSize="large" nzBlock class="gap" [nzLoading]="busy()" [disabled]="code().trim().length !== 6">
              Continue
            </button>
          </form>
          <p class="muted small">Didn't get it? Check your spam folder, or
            <button nz-button nzType="link" class="inline" (click)="sendCode()" [disabled]="busy()">send a new code</button>.</p>
        }

        @case ('choose') {
          <h1>Who is booking?</h1>
          <p class="lead">{{ verified()!.patients.length > 1
            ? 'More than one patient uses ' + shownContact() + '. Choose who you are signing in as.'
            : 'Continue as yourself, or register someone else who uses ' + shownContact() + '.' }}</p>
          <div class="choices">
            @for (patient of verified()!.patients; track patient.id) {
              <button nz-button nzSize="large" nzBlock (click)="choose(patient.id)" [nzLoading]="busy() && choosing() === patient.id"
                      [disabled]="busy()">
                <span nz-icon nzType="user"></span>{{ patient.displayName }}
              </button>
            }
            @if (verified()!.canRegister) {
              <button nz-button nzType="dashed" nzSize="large" nzBlock (click)="step.set('register')" [disabled]="busy()">
                <span nz-icon nzType="plus"></span>Someone else (new patient)
              </button>
            }
          </div>
          @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
        }

        @case ('register') {
          @if (verified()!.patients.length) {
            <button nz-button nzType="link" class="back" (click)="step.set('choose')"><span nz-icon nzType="left"></span>Back</button>
          }
          <h1>New patient</h1>
          <p class="lead">Tell us who the appointment is for. {{ context.clinic()?.name }} will see these details.</p>
          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="register()">
            <div class="row">
              <nz-form-item>
                <nz-form-label nzRequired nzFor="first">First name</nz-form-label>
                <nz-form-control nzErrorTip="Enter a first name.">
                  <input nz-input id="first" formControlName="firstName" autocomplete="given-name" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzRequired nzFor="last">Last name</nz-form-label>
                <nz-form-control nzErrorTip="Enter a last name.">
                  <input nz-input id="last" formControlName="lastName" autocomplete="family-name" />
                </nz-form-control>
              </nz-form-item>
            </div>
            <nz-form-item>
              <nz-form-label nzRequired nzFor="dob">Date of birth</nz-form-label>
              <nz-form-control nzErrorTip="Enter a date of birth.">
                <input nz-input id="dob" type="date" formControlName="dateOfBirth" autocomplete="bday" [max]="today" />
              </nz-form-control>
            </nz-form-item>
            @if (verified()!.channel === 'Email') {
              <nz-form-item>
                <nz-form-label nzRequired nzFor="phone">Mobile number</nz-form-label>
                <nz-form-control nzErrorTip="Enter a valid phone number.">
                  <input nz-input id="phone" type="tel" formControlName="phone" autocomplete="tel" placeholder="(212) 555-0142" />
                </nz-form-control>
              </nz-form-item>
            } @else {
              <nz-form-item>
                <nz-form-label nzFor="email">Email (optional)</nz-form-label>
                <nz-form-control nzErrorTip="Enter a valid email address.">
                  <input nz-input id="email" type="email" formControlName="email" autocomplete="email" />
                </nz-form-control>
              </nz-form-item>
            }
            <label nz-checkbox formControlName="smsOptIn" class="consent">
              Text me appointment confirmations and reminders. Message and data rates may apply. Reply STOP to opt out at any time.
            </label>
            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <button nz-button nzType="primary" nzSize="large" nzBlock class="gap" [nzLoading]="busy()">Create and continue</button>
          </form>
        }
      }
    </nz-card>
  `,
  styles: [`
    .card { max-width: 460px; margin: 0 auto; }
    h1 { font-size: 22px; margin: 0 0 var(--da-space-2); }
    .lead { color: var(--da-ink-2); margin-bottom: var(--da-space-4); }
    .label { display: block; font-weight: 500; margin-bottom: var(--da-space-1); }
    .code { font-size: 24px; letter-spacing: 0.4em; text-align: center; font-variant-numeric: tabular-nums; }
    .gap { margin-top: var(--da-space-3); }
    .back { padding-left: 0; margin-bottom: var(--da-space-2); }
    .inline { padding: 0; height: auto; }
    .muted { color: var(--da-ink-3); }
    .small { font-size: 13px; margin: var(--da-space-3) 0 0; }
    .choices { display: grid; gap: var(--da-space-2); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .consent { color: var(--da-ink-2); font-size: 13px; }
    @media (max-width: 420px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalSignInComponent {
  readonly context = inject(PortalContext);
  private readonly api = inject(PortalApiService);
  private readonly session = inject(PortalSessionService);
  private readonly router = inject(Router);

  readonly step = signal<Step>('contact');
  readonly contact = signal('');
  readonly code = signal('');
  readonly busy = signal(false);
  readonly choosing = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly sentText = signal('');
  readonly verified = signal<PortalVerifyResponse | null>(null);
  readonly today = new Date().toISOString().slice(0, 10);

  readonly shownContact = computed(() => {
    const v = this.verified();
    return v ? (v.channel === 'Sms' ? formatPhone(v.contact) : v.contact) : '';
  });

  readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    dateOfBirth: ['', Validators.required],
    phone: ['', Validators.pattern(PHONE_PATTERN)],
    email: ['', [Validators.email, Validators.maxLength(200)]],
    smsOptIn: [false]
  });

  constructor() {
    // Already signed in here: go straight to the visits page.
    if (this.session.isSignedInTo(this.context.slug())) void this.router.navigate(this.context.link());
  }

  sendCode(): void {
    const contact = this.contact().trim();
    if (!contact || this.busy()) return;
    this.run(this.api.start(this.context.slug(), contact), (response) => {
      const shown = response.data.channel === 'Sms' ? formatPhone(response.data.contact) : response.data.contact;
      this.sentText.set(`If ${shown} is on file with ${this.context.clinic()?.name ?? 'the clinic'}, we've sent a code to it. `
        + `It expires in ${response.data.codeValidMinutes} minutes.`);
      this.code.set('');
      this.step.set('code');
    });
  }

  verify(): void {
    if (this.busy()) return;
    this.run(this.api.verify(this.context.slug(), this.contact().trim(), this.code().trim()), (verified) => {
      this.verified.set(verified);
      if (verified.patients.length === 1 && !verified.canRegister) {
        this.choose(verified.patients[0].id);
      } else if (verified.patients.length === 0) {
        this.step.set('register');
      } else {
        this.step.set('choose');
      }
    });
  }

  choose(patientId: string): void {
    const ticket = this.verified()?.ticket;
    if (!ticket) return;
    this.choosing.set(patientId);
    this.run(this.api.select(this.context.slug(), ticket, patientId), (session) => this.signedIn(session));
  }

  register(): void {
    const verified = this.verified();
    if (!verified || this.busy()) return;
    const needsPhone = verified.channel === 'Email';
    this.form.controls.phone.setValidators(needsPhone ? [Validators.required, Validators.pattern(PHONE_PATTERN)] : [Validators.pattern(PHONE_PATTERN)]);
    this.form.controls.phone.updateValueAndValidity();
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    const v = this.form.getRawValue();
    this.run(this.api.register(this.context.slug(), {
      ticket: verified.ticket,
      firstName: v.firstName.trim(),
      lastName: v.lastName.trim(),
      dateOfBirth: v.dateOfBirth,
      phone: needsPhone ? v.phone.trim() : null,
      email: needsPhone ? null : optional(v.email),
      smsOptIn: v.smsOptIn
    }), (session) => this.signedIn(session));
  }

  restart(): void {
    this.step.set('contact');
    this.error.set(null);
    this.verified.set(null);
  }

  private signedIn(session: PortalSession): void {
    this.session.start(session);
    void this.router.navigate(this.context.link());
  }

  private run<T>(call: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    call.pipe(finalize(() => {
      this.busy.set(false);
      this.choosing.set(null);
    })).subscribe({
      next: done,
      error: (error: unknown) => {
        // An expired ticket means starting over.
        if (error instanceof HttpErrorResponse && error.status === 401 && this.step() !== 'contact') {
          this.restart();
          this.error.set('Your sign-in took too long. Please ask for a new code.');
          return;
        }
        this.error.set(saveErrorText(error));
      }
    });
  }
}
