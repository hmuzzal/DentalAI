import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { finalize } from 'rxjs';
import { APPOINTMENT_STATUS } from '../../core/appointment-status';
import { PortalAppointment } from '../../core/models/portal.models';
import { PortalSessionService } from '../../core/services/portal-session.service';
import { PortalApiService } from '../../core/services/portal.service';
import { PageStateComponent } from '../../shared/page-state.component';
import { formatPhone } from '../../shared/phone.pipe';
import { remoteData } from '../../shared/remote-data';
import { PortalContext } from './portal-context';

/** /p/:slug — the patient's upcoming and past visits, with cancel and reschedule while the clinic allows it. */
@Component({
  selector: 'app-portal-home',
  standalone: true,
  imports: [RouterLink, NzButtonModule, NzCardModule, NzIconModule, NzPopconfirmModule, NzTagModule, PageStateComponent],
  template: `
    <div class="head">
      <div>
        <h1>Hi {{ session.patient()?.firstName }}</h1>
        <p class="muted">Times are in the clinic's time zone.</p>
      </div>
      <a nz-button nzType="primary" nzSize="large" [routerLink]="context.link('book')"><span nz-icon nzType="plus"></span>Book a visit</a>
    </div>

    <app-page-state [status]="appointments.status()" (retry)="appointments.reload()" errorText="We could not load your visits.">
      <h2>Upcoming</h2>
      @for (item of upcoming(); track item.id) {
        <nz-card class="visit">
          <div class="visit__top">
            <div>
              <strong class="visit__when">{{ context.when(item.startTime) }}</strong>
              <div>{{ item.serviceName }} with {{ item.doctorName }}</div>
            </div>
            <nz-tag [nzColor]="status[item.status].color">{{ item.status === 'Pending' ? 'Awaiting confirmation' : status[item.status].label }}</nz-tag>
          </div>
          <div class="visit__actions">
            @if (item.canReschedule) {
              <a nz-button [routerLink]="context.link('appointments', item.id, 'reschedule')">Change time</a>
            }
            @if (item.canCancel) {
              <button nz-button nzDanger nz-popconfirm nzPopconfirmTitle="Cancel this appointment?" nzOkText="Cancel it"
                      nzCancelText="Keep it" (nzOnConfirm)="cancel(item)" [nzLoading]="cancelling() === item.id">Cancel</button>
            }
            @if (!item.canCancel) {
              <span class="muted small">To change this visit, {{ callText() }}</span>
            }
          </div>
        </nz-card>
      } @empty {
        <nz-card class="empty">
          <p>You have no upcoming visits.</p>
          <a nz-button nzType="primary" [routerLink]="context.link('book')">Book a visit</a>
        </nz-card>
      }

      @if (past().length) {
        <h2>Past and cancelled</h2>
        <ul class="past">
          @for (item of past(); track item.id) {
            <li>
              <span>{{ context.when(item.startTime) }}</span>
              <span class="muted">{{ item.serviceName }} · {{ item.doctorName }}</span>
              <nz-tag [nzColor]="status[item.status].color">{{ status[item.status].label }}</nz-tag>
            </li>
          }
        </ul>
      }
    </app-page-state>
  `,
  styles: [`
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--da-space-3); flex-wrap: wrap;
      margin-bottom: var(--da-space-4); }
    h1 { font-size: 24px; margin: 0; }
    h2 { font-size: 16px; margin: var(--da-space-4) 0 var(--da-space-2); }
    .muted { color: var(--da-ink-3); margin: 0; }
    .small { font-size: 13px; }
    .visit + .visit { margin-top: var(--da-space-3); }
    .visit__top { display: flex; justify-content: space-between; gap: var(--da-space-3); align-items: flex-start; }
    .visit__when { font-size: 16px; display: block; margin-bottom: 2px; }
    .visit__actions { display: flex; gap: var(--da-space-2); flex-wrap: wrap; align-items: center; margin-top: var(--da-space-3); }
    .empty { text-align: center; }
    .past { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--da-space-2); }
    .past li { display: flex; flex-wrap: wrap; gap: var(--da-space-1) var(--da-space-3); align-items: center; padding: var(--da-space-2) 0;
      border-bottom: 1px solid var(--da-border); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PortalHomeComponent {
  readonly context = inject(PortalContext);
  readonly session = inject(PortalSessionService);
  private readonly api = inject(PortalApiService);
  private readonly messages = inject(NzMessageService);

  readonly status = APPOINTMENT_STATUS;
  readonly cancelling = signal<string | null>(null);
  readonly appointments = remoteData(() => this.api.appointments(), [] as PortalAppointment[]);

  readonly upcoming = computed(() => this.appointments.value()
    .filter((a) => isOpen(a) && new Date(a.endTime).getTime() > Date.now())
    .sort((a, b) => a.startTime.localeCompare(b.startTime)));
  readonly past = computed(() => this.appointments.value().filter((a) => !this.upcoming().includes(a)));

  readonly callText = computed(() => {
    const phone = this.context.clinic()?.phone;
    return phone ? `please call ${formatPhone(phone)}.` : 'please call the clinic.';
  });

  cancel(item: PortalAppointment): void {
    this.cancelling.set(item.id);
    this.api.cancel(item.id).pipe(finalize(() => this.cancelling.set(null))).subscribe({
      next: () => {
        this.messages.success('Your appointment has been cancelled.');
        this.appointments.reload();
      },
      error: () => this.appointments.reload()
    });
  }
}

function isOpen(a: PortalAppointment): boolean {
  return a.status === 'Pending' || a.status === 'Confirmed';
}
