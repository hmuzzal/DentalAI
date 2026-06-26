import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NzAvatarModule } from 'ng-zorro-antd/avatar';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDropDownModule } from 'ng-zorro-antd/dropdown';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzLayoutModule } from 'ng-zorro-antd/layout';
import { NzMenuModule } from 'ng-zorro-antd/menu';
import { NzTypographyModule } from 'ng-zorro-antd/typography';
import { AuthStateService } from '../core/services/auth-state.service';
import { UserRole } from '../core/models/auth.models';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NzAvatarModule,
    NzButtonModule,
    NzDropDownModule,
    NzIconModule,
    NzLayoutModule,
    NzMenuModule,
    NzTypographyModule
  ],
  template: `
    <nz-layout class="shell">
      <nz-sider nzCollapsible nzTheme="light" class="shell__sider">
        <div class="shell__brand">
          <div class="shell__brand-mark">DA</div>
          <div>
            <h1>DentalAI</h1>
            <p>Clinic intelligence</p>
          </div>
        </div>

        <ul nz-menu nzMode="inline" class="shell__menu">
          @for (item of menuItems(); track item.path) {
            <li nz-menu-item [routerLink]="item.path" routerLinkActive="ant-menu-item-selected">
              <span nz-icon [nzType]="item.icon"></span>
              <span>{{ item.label }}</span>
            </li>
          }
        </ul>
      </nz-sider>

      <nz-layout>
        <nz-header class="shell__header">
          <div>
            <p class="shell__eyebrow">Multi-tenant operations</p>
            <h2>Operational command center</h2>
          </div>

          <div class="shell__user">
            <div>
              <strong>{{ authState.user()?.fullName ?? 'Guest' }}</strong>
              <p>{{ authState.user()?.role ?? 'No role' }}</p>
            </div>
            <button nz-button nzType="default" (click)="authState.logout()">Sign out</button>
          </div>
        </nz-header>

        <nz-content class="shell__content">
          <router-outlet />
        </nz-content>
      </nz-layout>
    </nz-layout>
  `,
  styles: [`
    .shell { min-height: 100vh; background: linear-gradient(180deg, #f7f6f2 0%, #eef3f5 100%); }
    .shell__sider { border-inline-end: 1px solid rgba(18, 52, 59, 0.08); padding: 1rem 0.75rem; }
    .shell__brand { display: flex; gap: 0.875rem; align-items: center; padding: 0.75rem 0.5rem 1.5rem; }
    .shell__brand h1 { margin: 0; font-size: 1rem; color: #12343b; }
    .shell__brand p { margin: 0.15rem 0 0; color: #59757b; font-size: 0.8rem; }
    .shell__brand-mark { display: grid; place-items: center; width: 2.75rem; height: 2.75rem; border-radius: 0.85rem; background: linear-gradient(135deg, #12343b, #2e7278); color: #fff; font-weight: 700; }
    .shell__menu { background: transparent; border: 0; }
    .shell__header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; padding: 1.5rem 2rem 0.5rem; background: transparent; height: auto; line-height: normal; }
    .shell__eyebrow { margin: 0 0 0.15rem; text-transform: uppercase; letter-spacing: 0.12em; font-size: 0.72rem; color: #6c878b; }
    .shell__header h2 { margin: 0; color: #12343b; }
    .shell__user { display: flex; align-items: center; gap: 1rem; }
    .shell__user p { margin: 0.15rem 0 0; color: #688084; font-size: 0.82rem; }
    .shell__content { padding: 1rem 2rem 2rem; }
    @media (max-width: 960px) {
      .shell__header { padding-inline: 1rem; align-items: flex-start; flex-direction: column; }
      .shell__content { padding-inline: 1rem; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppShellComponent {
  readonly authState = inject(AuthStateService);

  readonly menuItems = computed(() => {
    const role = this.authState.role();
    const items = [
      { label: 'Dashboard', path: '/dashboard', icon: 'dashboard' },
      { label: 'Doctors', path: '/doctors', icon: 'team' },
      { label: 'Services', path: '/services', icon: 'medicine-box' },
      { label: 'Patients', path: '/patients', icon: 'solution' },
      { label: 'Appointments', path: '/appointments', icon: 'calendar' }
    ];

    return role === 'SuperAdmin'
      ? [{ label: 'Clinics', path: '/clinics', icon: 'bank' }, ...items]
      : items;
  });
}
