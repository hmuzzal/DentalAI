import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { finalize } from 'rxjs';
import { PortalSettings } from '../../core/models/portal.models';
import { PortalSettingsService } from '../../core/services/portal.service';
import { saveErrorText, touchAll } from '../../shared/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';

const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** /settings/portal (clinic admins): turn the patient portal on, its address, and the online booking rules. */
@Component({
  selector: 'app-portal-settings-page',
  standalone: true,
  imports: [
    ReactiveFormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzFormModule, NzIconModule, NzInputModule, NzInputNumberModule,
    NzSkeletonModule, NzSwitchModule, PageHeaderComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Patient portal" eyebrow="Settings"
                       subtitle="Patients sign in with a code sent to their email or phone, then book, change or cancel visits and see their invoices." />

      @if (!saved()) {
        <nz-card><nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 8 }" /></nz-card>
      } @else {
        <nz-card>
          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()" class="form">
            <label class="switch">
              <nz-switch formControlName="enabled" />
              <span><strong>Online booking is {{ form.controls.enabled.value ? 'on' : 'off' }}</strong><br />
                <small>When it's off, the portal page says online booking isn't available and patients are signed out.</small></span>
            </label>

            <nz-form-item class="gap">
              <nz-form-label nzFor="slug">Portal address</nz-form-label>
              <nz-form-control nzErrorTip="3–40 lowercase letters, digits or dashes (no dash at the start or end).">
                <nz-input-group [nzAddOnBefore]="origin + '/p/'">
                  <input nz-input id="slug" formControlName="slug" autocomplete="off" />
                </nz-input-group>
              </nz-form-control>
            </nz-form-item>
            @if (saved()!.enabled) {
              <div class="link">
                <a [href]="link()" target="_blank" rel="noopener">{{ link() }}</a>
                <button nz-button type="button" nzSize="small" (click)="copy()"><span nz-icon nzType="copy"></span>Copy link</button>
              </div>
              <p class="muted">Put this link on your website, in emails and on social media.</p>
            }

            <div class="switches">
              <label class="switch"><nz-switch formControlName="allowNewPatients" />
                <span><strong>New patients can register</strong><br /><small>Otherwise only patients already on file (matched by email or mobile number) can sign in.</small></span></label>
              <label class="switch"><nz-switch formControlName="autoConfirm" />
                <span><strong>Confirm online bookings automatically</strong><br /><small>Otherwise they arrive as Pending, tagged Online, for the front desk to confirm.</small></span></label>
            </div>

            <div class="row">
              <nz-form-item>
                <nz-form-label nzFor="notice">Minimum notice (hours)</nz-form-label>
                <nz-form-control nzErrorTip="0 to 168 hours." nzExtra="The earliest time patients can book from now.">
                  <nz-input-number nzId="notice" formControlName="minNoticeHours" [nzMin]="0" [nzMax]="168" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzFor="ahead">Book up to (days ahead)</nz-form-label>
                <nz-form-control nzErrorTip="1 to 365 days.">
                  <nz-input-number nzId="ahead" formControlName="maxDaysAhead" [nzMin]="1" [nzMax]="365" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzFor="cutoff">Changes allowed until (hours before)</nz-form-label>
                <nz-form-control nzErrorTip="0 to 168 hours." nzExtra="After that, patients are asked to call.">
                  <nz-input-number nzId="cutoff" formControlName="cancelCutoffHours" [nzMin]="0" [nzMax]="168" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzFor="max">Upcoming visits per patient</nz-form-label>
                <nz-form-control nzErrorTip="1 to 20." nzExtra="The most a patient can have booked when booking online.">
                  <nz-input-number nzId="max" formControlName="maxUpcomingAppointments" [nzMin]="1" [nzMax]="20" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
            </div>
            <p class="muted">Choose which services can be booked online on the Services page ("Bookable online").</p>

            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <div class="actions">
              <button nz-button nzType="primary" [nzLoading]="saving()" [disabled]="form.pristine">Save settings</button>
            </div>
          </form>
        </nz-card>
      }
    </section>
  `,
  styles: [`
    .form { max-width: 680px; }
    .switch { display: flex; gap: var(--da-space-3); align-items: flex-start; cursor: pointer; }
    .switch small { color: var(--da-ink-2); font-size: 13px; }
    .switches { display: grid; gap: var(--da-space-3); margin: var(--da-space-4) 0; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 0 var(--da-space-3); }
    .full { width: 100%; }
    .gap { margin-top: var(--da-space-4); }
    .link { display: flex; align-items: center; gap: var(--da-space-2); flex-wrap: wrap; word-break: break-all; }
    .muted { color: var(--da-ink-2); font-size: 13px; }
    .actions { display: flex; justify-content: flex-end; }
    @media (max-width: 640px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalSettingsPageComponent {
  private readonly settingsService = inject(PortalSettingsService);
  private readonly messages = inject(NzMessageService);

  readonly origin = window.location.origin;
  readonly saved = signal<PortalSettings | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly link = computed(() => `${this.origin}${this.saved()?.path ?? ''}`);

  readonly form = inject(NonNullableFormBuilder).group({
    enabled: [false],
    slug: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(40), Validators.pattern(SLUG_PATTERN)]],
    allowNewPatients: [true],
    autoConfirm: [false],
    minNoticeHours: [2, [Validators.required, Validators.min(0), Validators.max(168)]],
    maxDaysAhead: [90, [Validators.required, Validators.min(1), Validators.max(365)]],
    cancelCutoffHours: [24, [Validators.required, Validators.min(0), Validators.max(168)]],
    maxUpcomingAppointments: [3, [Validators.required, Validators.min(1), Validators.max(20)]]
  });

  constructor() {
    this.settingsService.get().subscribe({
      next: (settings) => this.fill(settings),
      error: () => this.error.set('We could not load the portal settings.')
    });
  }

  save(): void {
    if (this.saving()) return;
    this.form.controls.slug.setValue(this.form.controls.slug.value.trim().toLowerCase());
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.settingsService.update(this.form.getRawValue())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (settings) => {
          this.fill(settings);
          this.messages.success(settings.enabled ? 'Saved. The patient portal is on.' : 'Saved. The patient portal is off.');
        },
        error: (error: unknown) => this.error.set(saveErrorText(error))
      });
  }

  copy(): void {
    void navigator.clipboard?.writeText(this.link()).then(() => this.messages.success('Link copied.'));
  }

  private fill(settings: PortalSettings): void {
    this.saved.set(settings);
    const { path: _path, ...values } = settings;
    this.form.reset(values);
  }
}
