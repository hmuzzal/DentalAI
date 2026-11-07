import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl, FormsModule, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { finalize } from 'rxjs';
import { MessageChannel, NotificationSettings } from '../../core/models/notification.models';
import { NotificationsService } from '../../core/services/notifications.service';
import { saveErrorText, touchAll } from '../../shared/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';

/** Every half hour, as "HH:mm:ss" values with 12-hour labels. */
const TIMES = Array.from({ length: 48 }, (_, i) => {
  const h = Math.floor(i / 2);
  const m = i % 2 ? 30 : 0;
  const label = `${h % 12 || 12}:${m ? '30' : '00'} ${h < 12 ? 'AM' : 'PM'}`;
  return { value: `${String(h).padStart(2, '0')}:${m ? '30' : '00'}:00`, label };
});

const MAX_HOURS = 72;

/** The second reminder (if any) must come closer to the appointment than the first. */
function soonerThanFirst(control: AbstractControl): ValidationErrors | null {
  const first = control.parent?.get('firstReminderHours')?.value as number | undefined;
  const second = control.value as number;
  return second > 0 && first != null && second >= first ? { order: true } : null;
}

/** /settings/notifications (clinic admins): which messages go out, when reminders are sent, quiet hours, and a test send. */
@Component({
  selector: 'app-notification-settings-page',
  standalone: true,
  imports: [
    FormsModule, ReactiveFormsModule, RouterLink, NzAlertModule, NzButtonModule, NzCardModule, NzCheckboxModule, NzFormModule,
    NzInputModule, NzInputNumberModule, NzRadioModule, NzSelectModule, NzSkeletonModule, NzSwitchModule, PageHeaderComponent
  ],
  template: `
    <section class="da-page">
      <app-page-header title="Reminders & messages" eyebrow="Settings"
                       subtitle="Confirmations and reminders sent to patients by email or text.">
        <a nz-button routerLink="/messages">View sent messages</a>
      </app-page-header>

      @if (!loaded()) {
        <nz-card><nz-skeleton [nzActive]="true" [nzParagraph]="{ rows: 8 }" /></nz-card>
      } @else {
        @if (notLive().length) {
          <nz-alert nzType="info" nzShowIcon class="gap"
                    [nzMessage]="notLive().join(' and ') + ' delivery is not connected yet.'"
                    nzDescription="Messages are still prepared and listed in the message log, but they are only written to the server log, not delivered. Delivery is switched on in the server configuration." />
        }

        <nz-card nzTitle="What to send">
          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()" class="form" id="notification-settings">
            <div class="switches">
              <label class="switch"><nz-switch formControlName="emailEnabled" /> <span><strong>Email</strong><br /><small>To patients with an email address.</small></span></label>
              <label class="switch"><nz-switch formControlName="smsEnabled" /> <span><strong>Text messages</strong><br /><small>Only to patients who agreed to texts. They can reply STOP.</small></span></label>
              <label class="switch"><nz-switch formControlName="confirmationsEnabled" /> <span><strong>Booking notices</strong><br /><small>When an appointment is booked, moved or cancelled.</small></span></label>
              <label class="switch"><nz-switch formControlName="remindersEnabled" /> <span><strong>Reminders</strong><br /><small>Before each appointment. Texts ask the patient to reply C to confirm.</small></span></label>
            </div>

            <div class="row">
              <nz-form-item>
                <nz-form-label nzFor="first-reminder">First reminder (hours before)</nz-form-label>
                <nz-form-control [nzErrorTip]="'1 to ' + maxHours + ' hours.'">
                  <nz-input-number nzId="first-reminder" formControlName="firstReminderHours" [nzMin]="1" [nzMax]="maxHours" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
              <nz-form-item>
                <nz-form-label nzFor="second-reminder">Second reminder (hours before)</nz-form-label>
                <nz-form-control nzErrorTip="Must be sooner than the first reminder." nzExtra="0 means no second reminder.">
                  <nz-input-number nzId="second-reminder" formControlName="secondReminderHours" [nzMin]="0" [nzMax]="maxHours" [nzPrecision]="0" class="full" />
                </nz-form-control>
              </nz-form-item>
            </div>

            <nz-form-item>
              <nz-form-label>Quiet hours for texts</nz-form-label>
              <nz-form-control nzExtra="In the clinic's time zone. Texts due in this window wait until it ends; a reminder that would arrive after the appointment is skipped. Emails are not held back.">
                <label nz-checkbox [ngModel]="quietOn()" (ngModelChange)="setQuiet($event)" [ngModelOptions]="{ standalone: true }">
                  Don't text patients overnight
                </label>
                @if (quietOn()) {
                  <div class="quiet">
                    <span>From</span>
                    <nz-select formControlName="quietHoursStart" class="time" nzAriaLabel="Quiet hours start">
                      @for (t of times; track t.value) { <nz-option [nzValue]="t.value" [nzLabel]="t.label" /> }
                    </nz-select>
                    <span>until</span>
                    <nz-select formControlName="quietHoursEnd" class="time" nzAriaLabel="Quiet hours end">
                      @for (t of times; track t.value) { <nz-option [nzValue]="t.value" [nzLabel]="t.label" /> }
                    </nz-select>
                  </div>
                }
              </nz-form-control>
            </nz-form-item>

            @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()!" class="gap" /> }
            <div class="actions">
              <button nz-button nzType="primary" [nzLoading]="saving()" [disabled]="form.pristine">Save settings</button>
            </div>
          </form>
        </nz-card>

        <nz-card nzTitle="Send a test message" class="test-card">
          <p class="muted">Check that messages reach a phone or inbox. Test messages appear in the message log.</p>
          <div class="test">
            <nz-radio-group [(ngModel)]="testChannel" nzButtonStyle="solid" aria-label="Send by">
              <label nz-radio-button nzValue="Email">Email</label>
              <label nz-radio-button nzValue="Sms">Text</label>
            </nz-radio-group>
            <input nz-input class="test-to" [(ngModel)]="testTo" (keydown.enter)="sendTest()"
                   [placeholder]="testChannel() === 'Sms' ? '+1 212 555 0142' : 'you@example.com'"
                   [attr.aria-label]="testChannel() === 'Sms' ? 'Mobile number' : 'Email address'" />
            <button nz-button (click)="sendTest()" [nzLoading]="testing()" [disabled]="!testTo().trim()">Send test</button>
          </div>
          @if (testResult(); as r) { <nz-alert [nzType]="r.ok ? 'success' : 'error'" nzShowIcon [nzMessage]="r.text" class="test-result" /> }
        </nz-card>
      }
    </section>
  `,
  styles: [`
    .form { max-width: 640px; }
    .switches { display: grid; gap: var(--da-space-3); margin-bottom: var(--da-space-4); }
    .switch { display: flex; gap: var(--da-space-3); align-items: flex-start; cursor: pointer; }
    .switch small { color: var(--da-ink-2); font-size: 13px; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: var(--da-space-3); }
    .full { width: 100%; }
    .quiet { display: flex; align-items: center; gap: var(--da-space-2); margin-top: var(--da-space-2); flex-wrap: wrap; }
    .time { width: 130px; }
    .gap { margin-bottom: var(--da-space-3); }
    .actions { display: flex; justify-content: flex-end; }
    .test-card { margin-top: var(--da-space-4); }
    .test { display: flex; gap: var(--da-space-2); flex-wrap: wrap; align-items: center; max-width: 640px; }
    .test-to { flex: 1 1 220px; }
    .test-result { margin-top: var(--da-space-3); max-width: 640px; }
    .muted { color: var(--da-ink-2); margin-top: 0; }
    @media (max-width: 640px) { .row { grid-template-columns: 1fr; } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NotificationSettingsPageComponent {
  private readonly notifications = inject(NotificationsService);
  private readonly messages = inject(NzMessageService);

  readonly times = TIMES;
  readonly maxHours = MAX_HOURS;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  private readonly saved = signal<NotificationSettings | null>(null);
  readonly loaded = computed(() => this.saved() !== null);
  readonly notLive = computed(() => {
    const s = this.saved();
    if (!s) return [];
    return [...(s.emailLive ? [] : ['Email']), ...(s.smsLive ? [] : ['Text message'])];
  });

  readonly form = inject(NonNullableFormBuilder).group({
    emailEnabled: [true],
    smsEnabled: [true],
    confirmationsEnabled: [true],
    remindersEnabled: [true],
    firstReminderHours: [24, [Validators.required, Validators.min(1), Validators.max(MAX_HOURS)]],
    secondReminderHours: [2, [Validators.required, Validators.min(0), Validators.max(MAX_HOURS), soonerThanFirst]],
    quietHoursStart: ['21:00:00'],
    quietHoursEnd: ['08:00:00']
  });
  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  /** Same start and end means no quiet hours. */
  readonly quietOn = computed(() => this.values().quietHoursStart !== this.values().quietHoursEnd);

  readonly testChannel = signal<MessageChannel>('Email');
  readonly testTo = signal('');
  readonly testing = signal(false);
  readonly testResult = signal<{ ok: boolean; text: string } | null>(null);

  constructor() {
    this.notifications.settings().subscribe({
      next: (settings) => this.fill(settings),
      error: () => this.error.set('We could not load the notification settings.')
    });
  }

  setQuiet(on: boolean): void {
    this.form.patchValue(on ? { quietHoursStart: '21:00:00', quietHoursEnd: '08:00:00' } : { quietHoursStart: '00:00:00', quietHoursEnd: '00:00:00' });
    this.form.markAsDirty();
  }

  save(): void {
    if (this.saving()) return;
    const v = this.form.getRawValue();
    if (this.form.invalid) {
      touchAll(this.form);
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.notifications
      .updateSettings(v)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (settings) => {
          this.fill(settings);
          this.messages.success('Notification settings saved.');
        },
        error: (error: unknown) => this.error.set(saveErrorText(error))
      });
  }

  sendTest(): void {
    const to = this.testTo().trim();
    if (!to || this.testing()) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.notifications
      .sendTest(this.testChannel(), to)
      .pipe(finalize(() => this.testing.set(false)))
      .subscribe({
        next: (response) => this.testResult.set({ ok: true, text: response.message }),
        error: (error: unknown) => this.testResult.set({ ok: false, text: saveErrorText(error) })
      });
  }

  private fill(settings: NotificationSettings): void {
    this.saved.set(settings);
    this.form.reset({
      emailEnabled: settings.emailEnabled,
      smsEnabled: settings.smsEnabled,
      confirmationsEnabled: settings.confirmationsEnabled,
      remindersEnabled: settings.remindersEnabled,
      firstReminderHours: settings.firstReminderHours,
      secondReminderHours: settings.secondReminderHours,
      quietHoursStart: settings.quietHoursStart,
      quietHoursEnd: settings.quietHoursEnd
    });
  }
}
