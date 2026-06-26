import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzGridModule } from 'ng-zorro-antd/grid';
import { NzListModule } from 'ng-zorro-antd/list';
import { NzStatisticModule } from 'ng-zorro-antd/statistic';
import { DashboardStats } from '../../core/models/dashboard.models';
import { DashboardService } from '../../core/services/dashboard.service';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, NzCardModule, NzGridModule, NzListModule, NzStatisticModule],
  template: `
    <section class="page">
      <div class="page__intro">
        <div>
          <p>Snapshot</p>
          <h1>Today’s clinic flow</h1>
        </div>
      </div>

      <div nz-row [nzGutter]="16">
        @for (card of statCards(); track card.label) {
          <div nz-col [nzXs]="24" [nzSm]="12" [nzLg]="6">
            <nz-card class="stat-card">
              <nz-statistic [nzTitle]="card.label" [nzValue]="card.value"></nz-statistic>
            </nz-card>
          </div>
        }
      </div>

      <div nz-row [nzGutter]="16" class="page__panels">
        <div nz-col [nzXs]="24" [nzLg]="14">
          <nz-card nzTitle="Daily operating focus" class="panel-card">
            <ul class="insights">
              <li>Keep reception focused on pending appointments that should be confirmed before noon.</li>
              <li>Use the appointments module to scan overlap risks and working-hour breaches at a glance.</li>
              <li>Dashboard totals update directly from the API, so this page is a live operational pulse.</li>
            </ul>
          </nz-card>
        </div>
        <div nz-col [nzXs]="24" [nzLg]="10">
          <nz-card nzTitle="Launch checklist" class="panel-card">
            <ul class="insights">
              <li>Apply the initial migration to SQL Server.</li>
              <li>Update the Angular API base URL token if the API host differs.</li>
              <li>Replace the development JWT key before deployment.</li>
            </ul>
          </nz-card>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .page { display: grid; gap: 1rem; }
    .page__intro p { margin: 0; text-transform: uppercase; letter-spacing: 0.12em; font-size: 0.72rem; color: #6b8589; }
    .page__intro h1 { margin: 0.35rem 0 0; color: #12343b; }
    .page__panels { margin-top: 0.25rem; }
    .stat-card, .panel-card { border-radius: 1.25rem; }
    .insights { margin: 0; padding-left: 1rem; color: #496469; display: grid; gap: 0.75rem; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardPageComponent {
  private readonly dashboardService = inject(DashboardService);
  readonly stats = signal<DashboardStats>({
    todayAppointments: 0,
    upcomingAppointments: 0,
    totalDoctors: 0,
    totalPatients: 0,
    totalServices: 0
  });

  constructor() {
    this.dashboardService.getStats().subscribe((stats) => this.stats.set(stats));
  }

  statCards() {
    const stats = this.stats();
    return [
      { label: 'Today', value: stats.todayAppointments },
      { label: 'Upcoming', value: stats.upcomingAppointments },
      { label: 'Doctors', value: stats.totalDoctors },
      { label: 'Patients', value: stats.totalPatients },
      { label: 'Services', value: stats.totalServices }
    ];
  }
}
