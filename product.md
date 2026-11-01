# DentalAI — Product Document

_Last updated: 2026-11-01_

## 1. Vision

DentalAI is a multi-tenant SaaS for dental clinics: scheduling, patients, billing and
reminders, with an AI receptionist that books and manages appointments by chat and voice.

**Market strategy:** launch **US-first** (English, US phone numbers, insurance, multiple
US time zones), then localise for **Bangladesh** (Bengali, WhatsApp, local payment
gateways, local clinic workflows).

## 2. Users and roles

| Role | Who | Main jobs |
|---|---|---|
| SuperAdmin | Platform operator | Manage clinics (tenants), support |
| ClinicAdmin | Clinic owner / office manager | Doctors, services, schedules, staff, reports |
| Receptionist | Front desk | Book / reschedule / cancel, check-in, patient records |
| Patient _(planned)_ | Clinic's patient | Self-booking, reminders, invoices (patient portal) |
| AI receptionist _(planned)_ | System agent | Answer questions, find slots, book on a patient's behalf |

## 3. Core principles

- **Tenant isolation:** every clinic-owned row carries `ClinicId`; every read and write is
  checked against the current user's clinic.
- **UTC in storage, clinic time in the UI:** all instants are stored as UTC. Each clinic
  has an IANA time zone (e.g. `America/New_York`). Schedules are entered in the clinic's
  local time and slots are computed in it, so DST is handled correctly.
- **The scheduling engine is the single source of truth** for availability. The UI,
  the patient portal and the AI receptionist all book through the same API and the same
  validation.
- **English-first and localisation-ready:** user-facing strings go through i18n so
  Bengali can be added later without rewriting screens.
- **Provider abstraction:** SMS, WhatsApp, email, payments and voice sit behind
  interfaces (`ISmsSender`, `IPaymentGateway`, …). US providers ship first and Bangladeshi
  providers plug in later.

## 4. Tech stack

- **API:** ASP.NET Core (.NET 10), Clean Architecture (Domain / Application /
  Infrastructure / Api), EF Core + SQL Server, FluentValidation, AutoMapper, JWT + refresh
  tokens, Serilog, Swagger.
- **Web:** Angular 20 (standalone components, signals), ng-zorro-antd, date-fns.
- **Tests:** xUnit (Application layer). Angular uses Karma/Jasmine; no specs yet.

## 5. Market feature requirements

### US launch (required for v1)
| Requirement | Meaning | Lands in |
|---|---|---|
| Multiple time zones | Each clinic has an IANA zone; all times are shown in clinic time, whatever the browser's zone | Phase 1 (finish) |
| English-first UI | All copy in English, with i18n scaffolding so other languages can be added | Phase 1 (finish) |
| US phone numbers | Validate and store phone numbers in E.164 (`+1XXXXXXXXXX`), display as `(XXX) XXX-XXXX`; needed for SMS | Phase 1.5 |
| Insurance fields | Patient insurance: carrier, member ID, group number, subscriber name, relationship and DOB, primary/secondary | Phase 1.5 (data), Phase 2 (claims and billing) |
| SMS reminders | Automated confirmations and reminders with opt-in/opt-out (TCPA), STOP handling | Phase 3 |
| AI receptionist | Chat and voice agent that books, reschedules and answers FAQs | Phases 6–7 |

### Bangladesh localisation (later)
- Bengali language (i18n locale `bn`)
- WhatsApp Business integration (reminders and AI chat channel)
- Local payment gateways (bKash, Nagad, SSLCommerz)
- Bangladeshi clinic workflows (walk-in queue / serial numbers, cash-first billing, no insurance)

## 6. Roadmap

| # | Phase | Status |
|---|---|---|
| 0 | Foundation: auth, multi-tenancy, clinics, doctors, services, patients, dashboard | ✅ Done (committed) |
| 1 | Scheduling engine | 🟡 ~80%: core built, uncommitted, gaps below |
| 1.5 | US readiness: E.164 phones, insurance profile (i18n scaffold done in Phase 1) | ⬜ Not started |
| 2 | Billing, invoices and payments (Stripe first) | ⬜ |
| 3 | Email and SMS reminders (Twilio / SendGrid, background jobs) | ⬜ |
| 4 | Reports and analytics | ⬜ (a basic dashboard exists) |
| 5 | Patient portal (self-booking) | ⬜ |
| 6 | AI receptionist: chat (Claude, tool use against the scheduling API) | ⬜ |
| 7 | Voice AI receptionist (telephony + speech) | ⬜ |
| 8 | Mobile app | ⬜ |
| 9 | Integrations (Google Calendar, Outlook, payment gateways) | ⬜ |
| 10 | Bangladesh localisation (Bengali, WhatsApp, bKash/Nagad, local workflows) | ⬜ |

## 7. Current status (audit, 2026-11-01)

**Build:** API ✅ builds · Angular ✅ builds (initial bundle 1.70 MB, over the 1.5 MB budget) · Tests ✅ 44 unit + 18 API integration pass (2026-11-01).
**Git:** Phase 0 and most of Phase 1 committed; the DST / time-zone / slot-interval / i18n slice is uncommitted.

### Done
- JWT auth with refresh tokens and roles (SuperAdmin, ClinicAdmin, Receptionist); tenant-scoped repositories
- CRUD for clinics, doctors, services and patients (API + Angular pages)
- Clinic `TimeZone` column (seed uses `Asia/Dhaka`)
- **Scheduling engine (uncommitted):**
  - `DoctorSchedule` (weekly hours), `DoctorBreak`, `DoctorLeave` entities + migration `AddSchedulingEngine`
  - `Appointment.ServiceId`: end time is derived from the service's duration
  - `SchedulingEngine`: 15-minute slot grid, excludes breaks, leave, booked slots and past times; validates bookings
  - Endpoints: `GET/PUT /doctor-schedules/{doctorId}`, `GET /appointments/available-slots`, calendar, scheduling dashboard
  - Angular: doctor-schedules page, calendar page, `time-slot-selector` component

### Gaps in Phase 1 (must fix before calling it done)
0. ~~**BLOCKER: the API drops `Appointments.ServiceId` on every startup.**~~ ✅ Fixed 2026-11-01.
   The ServiceId drop block and legacy check were removed from `AlignLegacyAppointmentSchemaAsync` in `Program.cs`.
   The idempotent migration `20261101132525_RepairAppointmentServiceId` re-adds the column, backfills it, and
   restores its index and FK. Verified: the column survives API startup, and `/appointments` and
   `/dashboard/scheduling` return 200.
   (Still open: migration `20260622093000_BackfillClinicTimeZone` has no `.Designer.cs`, so EF never runs it.)
1. ~~**No booking UI.**~~ ✅ Fixed 2026-11-01: "New appointment" drawer (header button, per-day "+" on
   Appointments and Calendar, empty-state actions). Conflicts show inline and reload the slots.
2. ~~**No reschedule.**~~ ✅ Fixed 2026-11-01: `POST /appointments/{id}/reschedule` (same doctor and service; Pending or
   Confirmed only; status kept). "Reschedule" on the Appointments table and Calendar cards opens the drawer in
   reschedule mode; `available-slots?excludeAppointmentId=` stops the appointment blocking its own slots.
3. ~~**Double-booking race.**~~ ✅ Fixed 2026-11-01: create and reschedule validate and save inside
   `IUnitOfWork.RunWithDoctorScheduleLockAsync` (transaction + `sp_getapplock` per doctor). Before: 8 parallel
   bookings of one slot all succeeded. After: 1 succeeds, the rest get 409 (also checked with 20).
4. **Times shown in browser time, not clinic time.** Mostly fixed 2026-11-01: dashboard, Appointments,
   Calendar and booking format and group by the user's clinic zone (`core/time-zone.ts`). The API now
   returns DateTimes as UTC (`Z`); before, EF read them back with no kind, so browsers parsed them as local time.
   Booking and rescheduling use the selected doctor's clinic zone (`DoctorDto.ClinicTimeZone`), so platform
   admins book correctly for any clinic.
   Appointment and calendar DTOs now carry `ClinicTimeZone`; the Appointments, Calendar and dashboard lists show
   each appointment in its own clinic's zone, tagged (e.g. "9:00 AM EST") when it differs from the viewer's.
   ✅ 2026-11-01: the calendar opens on (and its "Today" button returns to) the clinic's today, not the browser's.
5. ~~**Clinic time-zone picker.**~~ ✅ Fixed 2026-11-01: the Clinics page has an add/edit drawer with a searchable
   time-zone dropdown (from `GET /lookups/time-zones`: 8 main US zones first, then all others by current offset)
   and a slot-interval picker. The create/update validators reject zone IDs the server does not recognise.
6. ~~**DST edge cases.**~~ ✅ Fixed 2026-11-01: `SchedulingEngine.ToUtc` never throws. A wall-clock time repeated
   by fall-back means its first occurrence; one skipped by spring-forward is never offered as a slot
   (`TryToUtcExact`), and is read with the pre-jump offset elsewhere (e.g. midnight day boundaries). An appointment
   lasts its real duration in UTC; the working-hour and break checks use the wall-clock span `start + duration`.
   Tested on the 2026 and 2027 New York transitions.
7. ~~**Dashboard revenue grouped by UTC date.**~~ ✅ Fixed 2026-11-01: `DashboardCalculator` counts "today" and buckets revenue
   by day/month in each appointment's clinic zone (7 unit tests); the chart window ends on the clinic's today.
   Note: revenue now aggregates up to 12 months of completed appointments in memory; move to SQL if volumes grow.
8. ~~**Slot interval hard-coded at 15 minutes.**~~ ✅ Fixed 2026-11-01: `Clinics.SlotIntervalMinutes` (migration
   `AddClinicSlotInterval`, default 15; allowed 5, 10, 15, 20, 30, 60). Slots also carry `ClinicTimeZone`.
9. **Test coverage.** 44 unit tests (engine, DST, zones, slot interval, clinic validators, time-zone catalog) and
   18 API integration tests (`DentalAI.Api.IntegrationTests`): concurrent booking and rescheduling, the per-doctor
   lock, reschedule rules, slot exclusion, cancel, clinic zone on DTOs, time-zone lookup and validation, and New York
   spring-forward / fall-back days end to end. They boot the real API on a throwaway SQL Server database
   (`DENTALAI_TEST_SQLSERVER`, default "."). Disabling the lock makes the 3 concurrency tests fail, as it should.
   Still missing: tenant isolation.
10. ~~**Super admins got a 500 on every tenant table.**~~ ✅ Fixed 2026-11-01 (found by the new tests): the tenant
   query filter used `CurrentClinicId.Value`, which EF evaluates as a parameter even when the super-admin bypass
   applies, so it threw "Nullable object must have a value" for users without a clinic.

### Technical debt / known issues (noted 2026-11-01, open until fixed)
- [ ] **Security: vulnerable packages.** AutoMapper 13.0.1 (high, GHSA-rvv3-g6hj-g44x) and
  System.Security.Cryptography.Pkcs 9.0.4 (high GHSA-846f-f764-76mx, moderate GHSA-2fmv-rv2c-59x7). Upgrade.
- [ ] **Security: JWT secret in `appsettings.json`.** Move it to user-secrets (dev) and
  environment variables or a key vault (prod), then rotate the key.
- [ ] **`Program.cs` is ~980 lines of one-off DB patches.** The startup `AlignLegacy*Schema`
  raw-SQL methods should become migrations, or be deleted once all databases are migrated.
  2026-11-01: a fresh database could not be created at all (AddSchedulingEngine indexed a column the patches create
  later; InitialCreate's required `Clinics.Slug` broke every clinic insert). Fixed: startup now migrates to
  `SecureMultiTenantAuthAndPatientsV2`, runs the patches, then continues; migration `AllowNullClinicSlug`. The
  integration tests now prove a fresh install works. Note: the filtered `IX_Clinics_Slug` index needs
  QUOTED_IDENTIFIER ON for writes (SqlClient default; use `sqlcmd -I`).- [x] **Build/cache folders in git.** `.dotnet-cli`, `.pnpm-store`, `.nuget`, `.buildtmp`,
  `.appdata` and `.vs` show up in git. Add them to `.gitignore` and untrack them. ✅ Done 2026-11-01.
- [ ] No Angular unit tests. (API integration tests exist since 2026-11-01.)
- [ ] Angular initial bundle is 1.70 MB, over the 1.5 MB budget (build warning). Likely ng-zorro and the chart
  library on first load; lazy-load or trim imports.
- [ ] Migration `20260622093000_BackfillClinicTimeZone` has no `.Designer.cs`, so EF never runs it.
- [ ] i18n: only the clinic form is marked up (`i18n` / `$localize`). Run `pnpm extract-i18n` to update
  `src/locale/messages.xlf`; mark other screens as they are touched.

## 8. Phase plans

### Phase 1: finish the scheduling engine (next)
**Backend**
- [x] Atomic booking: validate + insert under `sp_getapplock` keyed on DoctorId; 409 on conflict (2026-11-01)
- [x] `POST /appointments/{id}/reschedule` (new start time, re-validated by the engine, excluding the appointment itself)
- [x] DST-safe `ToUtc`: skip non-existent local times, pick the first instance of ambiguous ones (2026-11-01)
- [x] Validate the clinic `TimeZone` as a real zone ID; endpoint `GET /lookups/time-zones` (US zones first) (2026-11-01)
- [x] Return the clinic time-zone ID with the slot, calendar and appointment DTOs (2026-11-01)
- [x] Clinic setting `SlotIntervalMinutes` (default 15; 5, 10, 15, 20, 30 or 60) (2026-11-01)
- [x] Dashboard: group revenue by clinic-local date (2026-11-01)
- [x] Tests: DST (America/New_York, March and November), multiple zones, overlap, leave, reschedule (2026-11-01)

**Frontend**
- [x] Booking flow: doctor → service → patient → date → `time-slot-selector` → confirm (2026-11-01)
- [x] Reschedule and cancel actions from the calendar and appointments list (2026-11-01)
- [x] Time-zone-aware formatting: helpers in `core/time-zone.ts` (`formatInZone`, `dateKeyInZone`)
- [x] Time-zone dropdown on the clinic form (new add/edit clinic drawer, with slot interval) (2026-11-01)
- [x] i18n scaffold (Angular `$localize`); English as the source locale (2026-11-01)

**Housekeeping**
- [x] `.gitignore` the generated folders, then commit Phase 0 cleanup and Phase 1 separately

### UI track (runs alongside Phase 1)
- [x] 1. Foundation: design tokens (`styles.scss`), ng-zorro teal theme, shared `app-page-header`,
      `app-page-state` (loading/empty/error), `remoteData()` loader
- [x] 2. App shell: grouped nav, clinic name and time zone in the header, user menu, collapsible sidebar, mobile drawer
- [x] 3. Fixes: menu icons, dashboard developer notes removed, dashboard uses the scheduling stats and revenue chart,
      route-based tab titles, favicon, global HTTP error toasts, seed login shown in development only
- [x] "+ New appointment" header button
- [ ] 4. Management pages: search, paging, add/edit side panel (Doctors, Patients, Services, Clinics)
- [ ] 5. Merge Appointments and Calendar; rebuild Doctor schedules as a weekly grid
- [x] 6. Booking flow (= Phase 1 gap 1). Also: ng-zorro locale set to English (it defaulted to Chinese)

### Phase 1.5: US readiness
- Phone: normalise to E.164 (libphonenumber-csharp), default region from clinic country, US display format
- `Clinic.Country` (default `US`)
- `PatientInsurance` entity (primary/secondary): carrier, member ID, group #, subscriber name/DOB, relationship to patient
- Patient fields: address, preferred contact method, SMS opt-in flag and timestamp (needed for TCPA)
- UI: insurance tab on the patient form

### Phase 2: billing, invoices and payments
- `Invoice`, `InvoiceLine`, `Payment`; generate an invoice from a completed appointment
- Insurance estimate vs. patient responsibility
- `IPaymentGateway` with Stripe first; PDF invoice

### Phase 3: email and SMS reminders
- Background job runner (Hangfire or a hosted service); `ISmsSender` (Twilio), `IEmailSender` (SendGrid)
- Rules: confirmation on booking, reminders 24 h and 2 h before; reply C to confirm / STOP to opt out
- Message log; quiet hours in the clinic's time zone

### Phases 4–10
Scoped in detail when each phase starts. Key dependencies:
- The AI receptionist (6) builds on stable booking/reschedule APIs (1) and patient lookup by phone (1.5).
- Voice (7) builds on chat (6) and telephony (3).

## 9. Out of scope (for now)
Clinical charting / odontograms, imaging (X-ray), e-prescriptions, and electronic claims
submission (EDI 837). These may come after Phase 9.
