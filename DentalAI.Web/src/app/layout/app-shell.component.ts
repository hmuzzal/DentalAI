import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { NzAvatarModule } from 'ng-zorro-antd/avatar';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { NzDropDownModule } from 'ng-zorro-antd/dropdown';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzLayoutModule } from 'ng-zorro-antd/layout';
import { NzMenuModule } from 'ng-zorro-antd/menu';
import { filter, map } from 'rxjs';
import { CLINIC_STAFF, USER_ROLE_LABELS, UserRole } from '../core/models/auth.models';
import { AuthStateService } from '../core/services/auth-state.service';
import { BookingService } from '../core/services/booking.service';
import { BookingDrawerComponent } from '../features/appointments/booking-drawer.component';
import { timeZoneLabel } from '../core/time-zone';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  /** Narrower than the group's roles, e.g. Staff is for clinic admins only. */
  roles?: UserRole[];
}

interface NavGroup {
  label: string;
  items: NavItem[];
  roles?: UserRole[];
}

const NAV_GROUPS: NavGroup[] = [
  { label: 'Overview', items: [{ label: 'Dashboard', path: '/dashboard', icon: 'dashboard' }] },
  {
    label: 'Scheduling',
    roles: CLINIC_STAFF,
    items: [
      { label: 'Appointments', path: '/appointments', icon: 'calendar' },
      { label: 'Doctor schedules', path: '/doctor-schedules', icon: 'clock-circle' }
    ]
  },
  {
    label: 'Clinic',
    roles: CLINIC_STAFF,
    items: [
      { label: 'Patients', path: '/patients', icon: 'solution' },
      { label: 'Doctors', path: '/doctors', icon: 'team' },
      { label: 'Services', path: '/services', icon: 'medicine-box' },
      { label: 'Staff', path: '/staff', icon: 'user', roles: ['ClinicAdmin'] }
    ]
  },
  { label: 'Platform', roles: ['SuperAdmin'], items: [{ label: 'Clinics', path: '/clinics', icon: 'bank' }] }
];

/** Below this width the sidebar becomes a slide-out drawer. */
const MOBILE_QUERY = '(max-width: 991.98px)';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    BookingDrawerComponent,
    NgTemplateOutlet,
    RouterOutlet,
    RouterLink,
    NzAvatarModule,
    NzButtonModule,
    NzDrawerModule,
    NzDropDownModule,
    NzIconModule,
    NzLayoutModule,
    NzMenuModule
  ],
  template: `
    <ng-template #brand let-compact>
      <a class="brand" routerLink="/dashboard" [class.brand--compact]="compact">
        <span class="brand__mark" aria-hidden="true">DA</span>
        @if (!compact) {
          <span class="brand__text"><strong>DentalAI</strong><small>Clinic workspace</small></span>
        }
      </a>
    </ng-template>

    <ng-template #nav let-collapsed>
      <ul nz-menu nzMode="inline" class="nav" [nzInlineCollapsed]="collapsed">
        @for (group of navGroups(); track group.label) {
          <li nz-menu-group [nzTitle]="collapsed ? '' : group.label">
            <ul>
              @for (item of group.items; track item.path) {
                <li nz-menu-item nzMatchRouter [routerLink]="item.path">
                  <span nz-icon [nzType]="item.icon"></span>
                  <span>{{ item.label }}</span>
                </li>
              }
            </ul>
          </li>
        }
      </ul>
    </ng-template>

    <nz-layout class="shell">
      @if (!isMobile()) {
        <nz-sider class="shell__sider" nzTheme="light" [nzWidth]="240" [nzCollapsedWidth]="72"
                  [nzCollapsed]="collapsed()" [nzTrigger]="null">
          <ng-container *ngTemplateOutlet="brand; context: { $implicit: collapsed() }" />
          <ng-container *ngTemplateOutlet="nav; context: { $implicit: collapsed() }" />
        </nz-sider>
      }

      <nz-drawer [nzVisible]="isMobile() && drawerOpen()" nzPlacement="left" [nzWidth]="264"
                 [nzClosable]="false" [nzBodyStyle]="{ padding: '12px 8px' }" (nzOnClose)="drawerOpen.set(false)">
        <ng-container *nzDrawerContent>
          <ng-container *ngTemplateOutlet="brand; context: { $implicit: false }" />
          <ng-container *ngTemplateOutlet="nav; context: { $implicit: false }" />
        </ng-container>
      </nz-drawer>

      <nz-layout class="shell__main">
        <nz-header class="shell__header">
          <div class="shell__context">
            <button nz-button nzType="text" class="shell__toggle" (click)="toggleNav()"
                    [attr.aria-label]="isMobile() ? 'Open menu' : (collapsed() ? 'Expand sidebar' : 'Collapse sidebar')">
              <span nz-icon [nzType]="isMobile() ? 'menu' : (collapsed() ? 'menu-unfold' : 'menu-fold')"></span>
            </button>
            <div class="clinic">
              <strong class="clinic__name">{{ clinicName() }}</strong>
              @if (clinicTimeZone()) {
                <span class="clinic__tz"><span nz-icon nzType="global"></span>{{ clinicTimeZone() }}</span>
              }
            </div>
          </div>

          <div class="shell__actions">
            @if (isClinicStaff()) {
              <button nz-button nzType="primary" (click)="booking.open()" aria-label="New appointment">
                <span nz-icon nzType="plus"></span><span class="shell__action-text">New appointment</span>
              </button>
            }

            <button class="user" nz-dropdown [nzDropdownMenu]="userMenu" nzTrigger="click" nzPlacement="bottomRight"
                    aria-label="Account menu">
              <nz-avatar [nzText]="initials()" [nzSize]="32" class="user__avatar" />
              <span class="user__text">
                <strong>{{ authState.user()?.fullName }}</strong>
                <small>{{ roleLabel() }}</small>
              </span>
              <span nz-icon nzType="down" class="user__caret"></span>
            </button>
            <nz-dropdown-menu #userMenu="nzDropdownMenu">
              <ul nz-menu>
                <li nz-menu-item nzDisabled class="user-menu__email">{{ authState.user()?.email }}</li>
                <li nz-menu-divider></li>
                <li nz-menu-item (click)="authState.logout()"><span nz-icon nzType="logout"></span> Sign out</li>
              </ul>
            </nz-dropdown-menu>
          </div>
        </nz-header>

        <nz-content class="shell__content">
          <router-outlet />
        </nz-content>
      </nz-layout>
    </nz-layout>

    @if (isClinicStaff()) { <app-booking-drawer /> }
  `,
  styles: [`
    :host { display: block; }
    .shell { min-height: 100vh; background: var(--da-bg); }

    /* Sidebar */
    .shell__sider { position: sticky; top: 0; height: 100vh; overflow-y: auto; background: var(--da-surface);
      border-inline-end: 1px solid var(--da-border); padding: var(--da-space-3) var(--da-space-2); }
    .brand { display: flex; align-items: center; gap: var(--da-space-3); padding: var(--da-space-2) var(--da-space-2) var(--da-space-4); color: inherit; }
    .brand--compact { justify-content: center; padding-inline: 0; }
    .brand__mark { flex: none; display: grid; place-items: center; width: 36px; height: 36px; border-radius: var(--da-radius);
      background: var(--da-brand-deep); color: #fff; font-weight: 700; font-size: 14px; letter-spacing: 0.02em; }
    .brand__text { display: grid; line-height: 1.2; }
    .brand__text strong { color: var(--da-ink); font-size: 15px; }
    .brand__text small { color: var(--da-ink-3); font-size: 12px; }

    .nav { border-inline-end: 0 !important; background: transparent; }
    :host ::ng-deep .nav .ant-menu-item-group-title { padding: 12px 16px 4px; font-size: 11px; font-weight: 600;
      text-transform: uppercase; letter-spacing: 0.08em; color: var(--da-ink-3); }
    :host ::ng-deep .nav.ant-menu-inline-collapsed .ant-menu-item-group-title { padding: 4px 0; margin: 4px 12px;
      border-top: 1px solid var(--da-border); }
    :host ::ng-deep .nav .ant-menu-item { border-radius: var(--da-radius-sm); margin-inline: 4px; width: calc(100% - 8px); }
    :host ::ng-deep .nav .ant-menu-item-selected { background: var(--da-brand-soft) !important; font-weight: 600; }
    :host ::ng-deep .nav .ant-menu-item-selected::after { display: none; }

    /* Header */
    .shell__main { min-width: 0; }
    .shell__header { position: sticky; top: 0; z-index: 10; display: flex; justify-content: space-between; align-items: center;
      gap: var(--da-space-4); height: 56px; line-height: normal; padding: 0 var(--da-space-5) 0 var(--da-space-3);
      background: var(--da-surface); border-bottom: 1px solid var(--da-border); }
    .shell__context { display: flex; align-items: center; gap: var(--da-space-2); min-width: 0; }
    .shell__toggle { color: var(--da-ink-2); }
    .clinic { display: flex; align-items: baseline; gap: var(--da-space-3); min-width: 0; }
    .clinic__name { color: var(--da-ink); font-size: 15px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .clinic__tz { display: inline-flex; align-items: center; gap: 6px; color: var(--da-ink-3); font-size: 13px; white-space: nowrap; }

    .shell__actions { display: flex; align-items: center; gap: var(--da-space-3); flex: none; }
    .shell__action-text { margin-left: 6px; }
    .user { display: flex; align-items: center; gap: var(--da-space-2); padding: 4px 8px; border: 0; border-radius: var(--da-radius);
      background: transparent; cursor: pointer; color: inherit; font: inherit; }
    .user:hover, .user:focus-visible { background: var(--da-surface-2); }
    .user__avatar { background: var(--da-brand); flex: none; }
    .user__text { display: grid; text-align: left; line-height: 1.2; }
    .user__text strong { font-size: 13px; color: var(--da-ink); }
    .user__text small { font-size: 12px; color: var(--da-ink-3); }
    .user__caret { font-size: 10px; color: var(--da-ink-3); }

    .shell__content { padding: var(--da-space-5); min-width: 0; }

    @media (max-width: 991.98px) {
      .shell__header { padding-inline: var(--da-space-2) var(--da-space-3); }
      .shell__content { padding: var(--da-space-4); }
    }
    @media (max-width: 640px) {
      .clinic__tz, .user__text, .user__caret, .shell__action-text { display: none; }
      .shell__content { padding: var(--da-space-3); }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppShellComponent {
  readonly authState = inject(AuthStateService);
  readonly booking = inject(BookingService);

  readonly isMobile = toSignal(
    inject(BreakpointObserver).observe(MOBILE_QUERY).pipe(map((state) => state.matches)),
    { initialValue: false }
  );
  readonly collapsed = signal(false);
  readonly drawerOpen = signal(false);

  readonly navGroups = computed(() => {
    const role = this.authState.role();
    const allowed = (roles?: UserRole[]) => !roles || (role !== null && roles.includes(role));
    return NAV_GROUPS
      .filter((group) => allowed(group.roles))
      .map((group) => ({ ...group, items: group.items.filter((item) => allowed(item.roles)) }))
      .filter((group) => group.items.length);
  });

  readonly isClinicStaff = computed(() => this.authState.hasAnyRole(CLINIC_STAFF));

  readonly clinicName = computed(() => {
    const user = this.authState.user();
    if (!user) return '';
    return user.clinicName ?? (user.role === 'SuperAdmin' ? 'DentalAI platform' : 'Your clinic');
  });

  readonly clinicTimeZone = computed(() => timeZoneLabel(this.authState.user()?.clinicTimeZone));

  readonly roleLabel = computed(() => {
    const role = this.authState.role();
    return role ? USER_ROLE_LABELS[role] : '';
  });

  readonly initials = computed(() =>
    (this.authState.user()?.fullName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('')
  );

  constructor() {
    // Close the mobile drawer after navigating.
    inject(Router)
      .events.pipe(filter((event) => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.drawerOpen.set(false));
  }

  toggleNav(): void {
    if (this.isMobile()) {
      this.drawerOpen.set(true);
    } else {
      this.collapsed.update((value) => !value);
    }
  }
}
