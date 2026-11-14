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
| SuperAdmin ("Platform admin") | Platform operator | Manage clinics (tenants), platform usage (counts only). **No access to clinic data** (see below) |
| ClinicAdmin | Clinic owner / office manager | Doctors, services, schedules, **staff (invites, roles)**, reports |
| Receptionist | Front desk | Book / reschedule / cancel, check-in, patient records |
| Patient _(planned)_ | Clinic's patient | Self-booking, reminders, invoices (patient portal) |
| AI receptionist _(planned)_ | System agent | Answer questions, find slots, book on a patient's behalf |

**Platform admins and clinic data (decided 2026-11-02).** Patient names, dates of birth, insurance and appointment
details are protected health information (HIPAA). DentalAI is each clinic's business associate, so platform staff get
only what they need: clinics and platform-wide counts. They cannot read or change any clinic's patients,
appointments, doctors, services or schedules:
- API: clinic-data endpoints use the `ClinicStaff` policy (ClinicAdmin, Receptionist). The tenant query filter has no
  bypass, so a user without a clinic sees no clinic rows even if an endpoint is misconfigured. The only cross-clinic
  reads are in `PlatformRepository`, which returns counts (`GET /platform/overview`).
- UI: platform admins see Platform overview and Clinics only; clinic routes are guarded.
- Planned: time-limited, audited **support access** to one clinic, approved by that clinic, before onboarding real clinics.

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
  Infrastructure / Api), EF Core + SQL Server, FluentValidation, hand-written DTO mappings, JWT + refresh
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
| 1 | Scheduling engine | ✅ Done 2026-11-01 |
| 1.5 | US readiness: E.164 phones, clinic country, patient address and SMS consent, insurance | ✅ Done 2026-11-01 |
| 2 | Billing, invoices and payments (provider-independent core; online payments via the clinic's own Stripe) | 🟡 2a and 2b done 2026-11-02; 2c online payments deferred (needs a Stripe account) |
| 3 | Email and SMS reminders (SMTP / Twilio, outbox and background worker) | ✅ Done 2026-11-05 (delivery needs provider credentials) |
| 4 | Reports and analytics | ✅ Done 2026-11-07 (practice report, receivables aging, CSV) |
| 5 | Patient portal (self-booking) | ✅ Done 2026-11-08 (code sign-in, booking, invoices) |
| 6 | AI receptionist: chat (Ollama or Gemini now; OpenAI and Claude later; tool use against the portal API) | ✅ Done 2026-11-13 (Ollama; Gemini untested; OpenAI and Claude later) |
| 7 | Voice AI receptionist (telephony + speech) | ⬜ |
| 8 | Mobile app | ⬜ |
| 9 | Integrations (Google Calendar, Outlook, payment gateways) | ⬜ |
| 10 | Bangladesh localisation (Bengali, WhatsApp, bKash/Nagad, local workflows) | ⬜ |

## 7. Current status (audit, 2026-11-01)

**Build:** API ✅ builds · Angular ✅ builds (initial bundle 1.72 MB, over the 1.5 MB budget) · Tests ✅ 57 unit + 42 API integration pass (2026-11-01).
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
- [x] **Security: vulnerable packages.** AutoMapper 13.0.1 (high, GHSA-rvv3-g6hj-g44x) and
  System.Security.Cryptography.Pkcs 9.0.4 (high GHSA-846f-f764-76mx, moderate GHSA-2fmv-rv2c-59x7). ✅ Done 2026-11-11:
  Pkcs now resolves to 10.0.13 (via the MailKit upgrade); AutoMapper removed (fixed versions need a paid licence) and
  replaced by `Application/Mappings/DtoMappings.cs`. `dotnet list package --vulnerable` is clean.
- [x] **Security: JWT secret in `appsettings.json`.** ✅ Done 2026-11-11: removed from `appsettings.json`; local dev uses a
  key in `appsettings.Development.json`, tests set their own. Startup refuses a missing key, one under 32 bytes, or (outside
  Development) any key ever committed to the repo. **Deploy:** set `Jwt__SecretKey` (env var or key vault) to a new random value.
- [x] **`Program.cs` is ~980 lines of one-off DB patches.** The startup `AlignLegacy*Schema`
  raw-SQL methods should become migrations, or be deleted once all databases are migrated.
  ✅ Done 2026-11-14: moved into migration `AlignLegacySchema` (ordered right after `SecureMultiTenantAuthAndPatientsV2`,
  where InitialCreate's old table shapes still need it), and startup now just runs `MigrateAsync` (`Program.cs` 1,094 →
  340 lines). Each table is aligned only if it still has the old shape, so databases the startup patches already
  aligned apply it out of order as a no-op. It can't be deleted while InitialCreate builds the old schema.
  2026-11-01: a fresh database could not be created at all (AddSchedulingEngine indexed a column the patches create
  later; InitialCreate's required `Clinics.Slug` broke every clinic insert). Fixed: startup now migrates to
  `SecureMultiTenantAuthAndPatientsV2`, runs the patches, then continues; migration `AllowNullClinicSlug`. The
  integration tests now prove a fresh install works. Note: the filtered `IX_Clinics_Slug` index needs
  QUOTED_IDENTIFIER ON for writes (SqlClient default; use `sqlcmd -I`).
- [x] **Build/cache folders in git.** `.dotnet-cli`, `.pnpm-store`, `.nuget`, `.buildtmp`,
  `.appdata` and `.vs` show up in git. Add them to `.gitignore` and untrack them. ✅ Done 2026-11-01.
- [ ] No Angular unit tests. (API integration tests exist since 2026-11-01.)
- [x] Angular initial bundle was 1.73 MB, over the 1.5 MB budget. ✅ Done 2026-11-11: now 1.22 MB (239 kB transferred).
  Global styles import only the ng-zorro components in use (`src/ng-zorro.less`; add a line there when using a new
  component), and the booking drawer is `@defer`-loaded instead of bundled into the app shell.
- [x] Migration `20260622093000_BackfillClinicTimeZone` had no `.Designer.cs`, so EF never ran it. ✅ Deleted 2026-11-12: its SQL
  is identical to the `AlignLegacyClinicSchemaAsync` startup patch, which already adds `Clinics.TimeZone` where missing.
- [x] Phones saved before 2026-11-01 are not E.164 (e.g. the demo clinic's `+1-555-0101`). They are normalised the
  next time each record is saved; before SMS (Phase 3) add a one-off backfill that normalises them or flags the ones that fail.
  ✅ Done 2026-11-12: `PatientPhoneBackfill` runs at startup and converts them in each clinic's country (portal sign-in by
  phone matches E.164 only). Unreadable numbers are left as they are, logged, and shown to staff as "Check phone"
  (`PatientDto.PhoneNeedsReview`).
- [ ] **Support access** for platform admins: time-limited, approved by the clinic, every read and change written to an
  audit log. Needed before onboarding real clinics (platform admins currently have no way to help with clinic data).
- [x] **Staff accounts and invites** (2026-11-02). No self sign-up (`POST /auth/register` removed). People join only
  through a single-use invite link (256-bit token; only its SHA-256 is stored; expires in 7 days; a new link replaces
  the old one). The invited person sets their own password (10+ characters, a letter and a number).
  - Platform admin: New clinic includes the **first admin**; Clinics → Staff lists a clinic's accounts, invites admins,
    makes new links, deactivates/reactivates. No role changes, no receptionist invites, no clinic data.
  - Clinic admin: **Staff** page (`/staff`): invite admins or receptionists, change roles, new links,
    deactivate/reactivate. Can't change their own role or deactivate themselves, or remove the last active admin.
  - Deactivating or changing a role ends that person's refresh token. Invited users can't sign in until they accept.
  - Deleting a clinic deactivates its staff, ends their sessions and voids pending invites; sign-in, token refresh and
    `/auth/me` also refuse staff of a deleted clinic.
- [x] **Invite emails** ✅ 2026-11-05 (Phase 3): invites and new links are emailed; the logged copy drops the link once sent. Still open: "Forgot password",
  which can reuse the invite-token mechanism.
- [x] Access tokens stay valid until they expire (60 minutes) after someone is deactivated or their clinic is deleted.
  The next page load ends the session (`/auth/me` returns 401), but API calls with the old token work until expiry.
  If that window matters, check the user's status per request (cached) or shorten the token lifetime.
  ✅ Done 2026-11-12: every staff request checks the account is active, its clinic exists and the role and clinic match
  the token (`StaffAccessValidator`, cached 30 s). Deactivating, changing a role or deleting a clinic clears the cache, so
  the old token gets 401 at once. With several API servers, other servers notice within 30 s.
- [ ] Week start is fixed to Sunday (US). Make it a clinic setting for the Bangladesh phase (Saturday).
- [ ] i18n: only the clinic form is marked up (`i18n` / `$localize`); the Doctors, Patients and Services screens are not yet. Run `pnpm extract-i18n` to update
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
- [x] 4. Management pages: search, paging, add/edit side panel (Doctors, Patients, Services, Clinics) (2026-11-01)
  - Shared `app-form-drawer` (title, Cancel/Save footer, inline save error, full width on phones) and `shared/forms.ts`.
  - Doctors and Services: client-side search and paging (20 per page); Services also filter Active/Inactive.
  - Patients: server-side search (every word must match name, phone or email) and paging via
    `GET /patients/search?search=&page=&pageSize=` (max 100), because patient lists grow without limit.
  - Delete with a confirm popover (soft delete). Deleting a doctor with upcoming Pending/Confirmed appointments
    returns 409 with the count. (Super-admin Clinic column and clinic picker removed 2026-11-02: platform admins no
    longer see clinic data.)
  - API: doctor, patient and service requests have no `clinicId`: always the signed-in user's clinic (2026-11-02).
  - Checked in the browser as a super admin (add, edit, search, paging, page size, delete, empty states).
    Clinic-admin view (no Clinic column or clinic field) checked later the same day on a throwaway database.
  - Fixed with it: role-restricted pages (Clinics) bounced super admins to the dashboard on a refresh or direct link,
    because the role guard ran before the profile loaded. The guard now loads the profile first.
- [x] 5. Merge Appointments and Calendar; rebuild Doctor schedules as a weekly grid (2026-11-01)
  - `/appointments`: Day / Week / Month range, Calendar / List layout, Today and previous/next, doctor filter; state in
    the URL. Weeks run Sunday–Saturday (API too). Month cells show 3 appointments, then "+N more" opens the day.
    `/calendar` redirects here; the Calendar nav item is gone.
  - Doctor schedules: weekly grid (Sun–Sat) with open/closed, split shifts, breaks, a timeline bar and copy to
    Mon–Fri / every day; time off with date ranges; validation before saving; unsaved-changes bar and a prompt when
    switching doctors; `?doctor=` deep link and an "Hours" link on the Doctors page.
- [x] 6. Booking flow (= Phase 1 gap 1). Also: ng-zorro locale set to English (it defaulted to Chinese)

### Phase 1.5: US readiness
- [x] Phone: normalise to E.164 (libphonenumber-csharp), default region from clinic country, US display format (2026-11-01)
  - `PhoneNumberRules` (Application): clinic, doctor and patient phones are stored as E.164; numbers without +code are
    read in the clinic's country, invalid ones return 400. UI shows US numbers as (212) 555-0142 (`phone` pipe), and
    the phone fields say which country a number without a code is read as.
  - Patient search matches phone digits, so "(646) 555-0199" finds +16465550199.
- [x] `Clinic.Country` (default `US`; existing Asia/Dhaka clinics set to `BD`); `GET /lookups/countries` (US first);
  Country field on the clinic form; `UserProfile.clinicCountry` (2026-11-01)
- [x] `PatientInsurance` entity (primary/secondary): carrier, member ID, group #, subscriber name/DOB, relationship to
  patient. Synced by priority (updated in place); filtered unique index on (PatientId, Priority) where not deleted (2026-11-01)
- [x] Patient fields: address (street, apt, city, state, ZIP), preferred contact method (phone/text/email), SMS opt-in
  with `SmsOptInAtUtc` / `SmsOptOutAtUtc` recorded by the server (TCPA). Text needs consent; email needs an address (2026-11-01)
- [x] UI: patient form in tabs (Details, Contact, Insurance); loads the full record (`GET /patients/{id}`) when editing;
  tabs with problems get a red dot and Save jumps to the first one (2026-11-01)
- Migration `AddUsReadiness`. Checked in the browser on a throwaway database (clinic admin and super admin).

### Phase 2: billing, invoices and payments (planned 2026-11-02)

**Constraint: Stripe doesn't accept Bangladesh-registered businesses.** So billing doesn't depend on any payment
provider, and online card payments go to each **clinic's own** Stripe account (the clinic is the merchant; DentalAI
never holds patients' money). DentalAI's own subscription billing (clinics paying us) needs a company in a
Stripe-supported country (e.g. a US company via Stripe Atlas) or a merchant of record such as Paddle: a business
decision, outside Phase 2. Bangladesh Bank allows setting up a company abroad with up to USD 10,000; check tax and
repatriation with a Bangladeshi accountant first.

**2a. Billing core (API), no provider** ✅ Done 2026-11-02 (migration `AddBilling`; 24 unit + 12 API tests)
- `Invoice`: clinic, patient, optional appointment, number per clinic (`INV-000123`, allocated atomically), status
  (Draft → Issued → PartiallyPaid → Paid, or Void), issue and due dates, currency (clinic setting, USD), subtotal,
  discount, total, **insurance estimate**, **patient responsibility**, amount paid, balance, notes.
- `InvoiceLine`: service (optional), description, optional CDT code and tooth number, quantity, unit price, line total,
  insurance estimate per line.
- `Payment`: invoice, amount, method (cash, card terminal, check, insurance, online), reference (check number, card last
  4, Stripe ID), received date, recorded by. Refunds as `Refund` records linked to a payment, not edits.
- Rules: drafts are editable; issued invoices change only by payment, refund or void (void needs a reason). Payments
  can't exceed the balance. Status and balance are derived from lines and payments in one place. Invoice and payment
  writes run under a per-invoice lock, so concurrent payments can't overpay.
- **Create from a completed appointment**: one line from the service and its price. Lines can be added (multiple
  procedures in a visit).
- Insurance estimate: entered per line, or a coverage % from the patient's primary plan applied to the lines.
  Claims (837D/EDI) are later, not Phase 2.
- Clinic billing settings: currency, invoice prefix, payment terms (days), default tax rate (0: US dental services are
  generally not taxed).
- Permissions: receptionists create, issue and record payments; refunds and voids are clinic admins only.
- Dashboard revenue switches from "completed appointments × service price" to **payments received**.
- Tests: numbering under concurrency, overpayment and concurrent payments, status transitions, refunds, totals, tenant isolation.

- Built as planned, plus: **currency is a clinic setting** (ISO 4217, default from the clinic's country: US → USD,
  BD → BDT; amounts rounded to the currency's decimals, e.g. 0 for JPY, 3 for KWD). Each invoice keeps its own
  currency. **Tooth numbering** (Universal for US clinics, FDI elsewhere) is a setting too. Invoice numbers are given on
  issue (drafts have none). Lock timeouts now return 409 instead of 500.
- Endpoints: `/invoices` (list with outstanding totals per currency, get, create, `from-appointment/{id}`, update and
  delete drafts, `issue`, `payments`, `payments/{id}/refunds`, `void`), `/billing/settings`, `/billing/currencies`.
- UI already done: **Billing settings** page for clinic admins (`/settings/billing`); prices on Services, the booking
  drawer and the dashboard use the clinic's currency; dashboard revenue reads "Payments received".
- CDT codes are the ADA's copyright: clinics can type codes, but DentalAI must not ship the code list without a licence.

**2b. Billing UI** ✅ Done 2026-11-02
- Billing nav group: **Invoices** (filter by status, patient and dates; totals outstanding), **invoice detail** (lines,
  payments, record payment, refund, void), "Create invoice" from a completed appointment and from a patient, patient
  balance on the patient record.
- **Printable invoice** (print stylesheet → browser "Save as PDF"). Server-side PDF (QuestPDF) later if needed.
- Built: Billing nav group (Invoices, Billing settings). Invoices list (All / Drafts / Unpaid / Paid / Void, search,
  `?patient=` filter, outstanding total per currency). Invoice page (lines, totals, payments with refunds; Edit / Issue /
  Delete for drafts, Record payment, Refund and Void for admins, Print). Draft editor (patient search, lines with
  service, code, tooth, quantity, price, insurance estimate; "insurance covers %"; discount, tax, notes; live preview).
  "Invoice" on completed appointments opens their invoice or creates the draft. Patients: "Invoices" link and
  "Balance due" on the patient record. Printable invoice at `/invoices/{id}/print` (`GET /invoices/{id}/document`).
- Fixed with it: date-only values (date of birth, invoice dates) showed a day early for users east of UTC, from the
  date pipe with a `'UTC'` zone. New `dateOnly` pipe for all date-only values.

**2c. Online payments (needs a Stripe account to develop and test)**
- `IPaymentGateway` (create a payment link or checkout session, verify a webhook, refund). `StripeGateway` uses the
  **clinic's own** restricted API key and webhook secret, stored encrypted (ASP.NET Data Protection) in clinic billing
  settings; cards are entered only on Stripe's hosted Checkout (no card data touches DentalAI).
- "Send payment link" on an invoice; the webhook records the payment idempotently (by Stripe event ID).
- Later, with a company in a supported country: Stripe Connect (clinics onboard through Stripe), Stripe Terminal for
  in-clinic cards, and DentalAI's own subscription billing.

### Phase 3: email and SMS reminders ✅ Done 2026-11-05
- **Outbox.** Every email and text is a row in `OutboundMessages`, which is also the message log. Booking, rescheduling,
  cancelling and staff invites add rows; a `NotificationWorker` (hosted service in the API) sends due rows every 10 s and
  queues reminders every minute. Sending holds an app lock (`notification-dispatch`), so two servers never send the same
  message. Retries after 1, 5, 15 and 60 minutes, then Failed. Turn the worker off with
  `Notifications:BackgroundJobsEnabled=false`.
- **Rules.** Booking confirmation; reminders at 24 h and 2 h before (clinic setting, 1–72 h, second can be off). Only the
  latest due reminder is sent, never one that was already past when the visit was booked, never twice. A reschedule or
  cancellation withdraws pending messages and sends a notice. Texts only to patients with SMS consent and an E.164 phone,
  otherwise email; nothing if neither. Texts wait for the end of quiet hours (default 9 PM–8 AM, clinic time zone); a
  reminder that would arrive after the visit is Skipped.
- **Replies** (`POST /api/webhooks/twilio/sms`, Twilio signature required, 403 otherwise): STOP and the other carrier
  keywords withdraw SMS consent for every patient with that number; START gives it back; C / YES confirms the
  appointment from the latest appointment text (last 3 days).
- **Providers.** `Notifications:Email:Provider` = `Log` (default) or `Smtp` (MailKit: works with SendGrid, Postmark, SES,
  Microsoft 365). `Notifications:Sms:Provider` = `Log` or `Twilio` (REST; Messaging Service SID or From number). Keep
  passwords and the Twilio auth token in user-secrets or environment variables. Set `Notifications:WebBaseUrl` for invite
  links and `Sms:Twilio:InboundWebhookUrl` to the public webhook URL (the signature is computed over it).
- **UI.** **Reminders** page for clinic admins (`/settings/notifications`): channels, booking notices, reminder hours,
  quiet hours, "not connected yet" notice while a provider is `Log`, send a test. **Messages** page for staff
  (`/messages`, `?patient=` / `?appointment=`): status, recipient, full text.
- **Before real texts in the US:** register the brand and campaign for **A2P 10DLC** in Twilio (or use a toll-free
  number with toll-free verification). A non-US business can register with its own country's tax ID; vetting takes
  days to weeks. Unregistered 10DLC traffic is blocked by US carriers.
- Open: "Forgot password" email; phones saved before 2026-11-01 that aren't E.164 get email instead of texts until the
  record is saved again (or a backfill); bounce / delivery-status webhooks (`StatusCallback`) not handled yet; email
  replies go nowhere (no-reply sender).

**Error responses (2026-11-05).** Unexpected errors return 500 with "Something went wrong on our side… reference
{trace id}" and log the exception under that ID; the exception text is only included in Development. Lock timeouts
stay 409; requests the client aborted get no response.

### Phase 4: reports ✅ Done 2026-11-07
- **Reports** page (`/reports`, clinic staff), clinic time zone throughout, clinic billing currency only (invoices in
  other currencies are counted and flagged, not summed).
- **Practice** tab for any range up to 366 days (presets: this month, last month, last 90 days, this year, custom):
  production (issued, not void invoices by issue date; subtotal minus discounts, before tax), collections (payments
  minus refunds, by the day they happened), booked / completed / cancelled / no-shows, no-show rate (no-shows ÷
  completed + no-shows), cancellation rate (cancelled ÷ booked), new patients, patients seen; each compared with the
  previous range of the same length. Trend by day (≤ 31 days), week (≤ 92, Sunday start) or month, with a table view.
  Breakdowns by doctor (through the invoice's appointment; "No appointment linked" otherwise), by service (invoice
  lines before discounts) and by payment method.
- **Receivables** tab: balances owed today, by days past the due date (not yet due, 1–30, 31–60, 61–90, 90+), per
  patient, most overdue first; links to the patient's unpaid invoices.
- CSV download for the trend, doctors, services and receivables (formula-safe cells).
- API: `GET /api/reports/practice?from=&to=` (defaults to this month), `GET /api/reports/receivables`.
- **Who sees money:** practice money is clinic admins only. Receptionists get the Practice tab with visit and patient
  numbers (booked, completed, cancellations, no-shows, patients seen, new patients, service quantities); the API sends
  every money figure as null, no payment methods and `includesMoney: false`. The dashboard revenue card is admins only
  too (`revenueByDay`/`revenueByMonth` are null for receptionists). Receivables stay open to receptionists: they take
  payments and chase balances.
- Later: insurance vs patient portions in aging; doctor utilisation (booked ÷ scheduled hours); scheduled email of reports.

### Phase 5: patient portal ✅ Done 2026-11-08
Patients of a clinic sign in on the clinic's own portal page, book, reschedule and cancel, and see their invoices.
Same booking API and engine as the front desk (core principle 3).

**Decisions**
- **One portal per clinic** at `/p/{slug}` (e.g. `/p/smile-dental`). Off until the clinic's admin turns it on.
- **No passwords.** A patient types their email or mobile number and gets a 6-digit code by email or text. Codes expire after
  10 minutes, allow 5 tries, are stored hashed, and are removed from the message log once sent. At most 3 codes per
  contact per 15 minutes; the sign-in endpoints are also rate-limited per IP.
- **No account enumeration.** The answer is always "if that's on file, we sent a code". When new patients are allowed,
  a code is sent to unknown contacts too, and after verifying they fill a short form (name, date of birth, phone, email) that
  creates the patient record.
- **Shared contacts (families).** One phone or email can belong to several patients. After the code, the person picks
  who they're booking for (first name and last initial); each patient has their own session.
- **Separate patient tokens.** Patient access tokens have their own audience and authentication scheme, so they're
  rejected by every staff endpoint, and staff tokens by every portal endpoint. 30-minute access token, 30-day refresh
  token (hashed, rotated on use, revocable), in a `PatientPortalSessions` table. The token carries the clinic, so the
  tenant filter works as for staff; every portal query is also limited to the signed-in patient.
- **Clinic rules** (`ClinicPortalSettings`, admins): enabled, slug, allow new patients, auto-confirm online bookings
  (default off: they arrive as Pending for the front desk), minimum notice (default 2 h), how far ahead (default 90
  days), cancel/reschedule cutoff (default 24 h before), most upcoming online bookings per patient (default 3). Each service has an
  "Bookable online" switch (default on).
- **Staff see where a booking came from:** `Appointment.Source` (Staff or Portal); "Online" tag in the Appointments list.
- Patients see invoices that are issued, partly paid or paid (not drafts or void), with no staff-only fields.

**5a: API**
- [x] Entities and migration: `ClinicPortalSettings`, `PortalSignInCode`, `PatientPortalSession`, `Service.BookableOnline`,
  `Appointment.Source`; `MessageKind.PortalCode`
- [x] `GET /portal/{slug}`: clinic name, phone, address, time zone, whether new patients may register (anonymous)
- [x] `POST /portal/{slug}/auth/start` (send code), `/verify` (code → patients to choose or a registration ticket),
  `/select`, `/register`, `/refresh`, `/logout`
- [x] `GET/PUT /portal/me` (profile: contact details, address, SMS consent)
- [x] `GET /portal/services`, `/portal/doctors`, `/portal/slots?doctorId&serviceId&date`
- [x] `GET /portal/appointments`, `POST /portal/appointments`, `POST /portal/appointments/{id}/reschedule`, `/cancel`
- [x] `GET /portal/invoices`, `GET /portal/invoices/{id}`
- [x] Staff: `GET/PUT /portal-settings` (admins); `BookableOnline` on services; `Source` on appointments
- [x] Tests: sign-in (expiry, attempts, throttle, enumeration-safe answers, shared contacts, registration), token
  separation both ways, patient isolation (another patient's appointment and invoice → 404), clinic rules (disabled,
  notice, horizon, cutoff, limit), booking through the engine (conflict → 409)

**5b: UI**
- [x] Portal pages outside the staff shell: sign in (contact → code), choose patient, register, home (upcoming visits),
  book (service → doctor → day → time → confirm), reschedule / cancel, invoices, profile. Mobile-first.
- [x] Separate portal session (own storage key and interceptor branch; a portal 401 never signs staff out)
- [x] Staff: Settings › Patient portal (switches, rules, slug, copy link); "Bookable online" on the service form;
  "Online" tag on appointments

Not yet tested: the per-IP sign-in rate limit (raised in tests), and the portal UI in a browser (Angular builds; API flow checked end to end).
Later: pay invoices online (needs 2c), intake forms, "first available doctor", insurance card upload, Bengali.

### Phase 6: AI receptionist (chat) ✅ Built 2026-11-13; tried end to end with Ollama (chat page not yet clicked through in a browser)
A chat page in the patient portal ("Ask us") where a signed-in patient can ask questions, find open times, and book,
move or cancel their own visits. Voice comes in Phase 7.

**Decisions**
- **Provider-neutral.** The assistant talks to an `IChatModel` interface. `Ai:Provider` chooses the model:
  `None` (default, the assistant is hidden), `Ollama` (a local model, free, nothing leaves the machine) or `Gemini`
  (Google, free tier). `OpenAI` and `Claude` are reserved: their settings already exist, but the API refuses to
  start with them until their adapters are written. Development uses Ollama (`qwen2.5:7b`).
- **The model can only do what the patient can do.** Every tool calls `PortalService`, so all portal rules apply:
  the signed-in patient's records only, bookable-online services, notice, how far ahead, cut-off, the upcoming-visit
  limit, the scheduling engine and its lock. A weak model makes the assistant less helpful, never less safe.
- **Confirm first, enforced by the server.** Booking, moving and cancelling take two steps. The first tool call only
  checks the change against every rule, including whether the time is still open (`PortalService.Preview*`). It saves
  nothing, stores the change as pending and returns `needs_confirmation` with the exact change ("Book Cleaning with
  Dr. Sarah Ahmed on Fri, Nov 20 at 2:15 PM"). When the patient's very next message says yes ("yes", "ok go ahead"; not
  "no", "don't", "instead"), **the server makes the stored change itself** and shows the model the result. In real tests
  qwen2.5:7b often didn't make the second call, so nothing was booked. Online bookings still arrive as Pending unless the
  clinic auto-confirms.
- **The server also proposes a picked time.** When the patient names one time right after times were offered ("can I have
  2:15?"), the server checks that exact time with the last search's service, dentist and day. If exactly one dentist has it
  open, the server proposes it (stored as pending), so the model only has to ask. The model kept answering "Would you like to
  confirm?" in its own words without proposing anything. "Anything after 3?" and two times are left to the model.
- **Guards on what the model says** (each sends the model a private note, up to 2 per message): it claims a booking, move
  or cancellation that no tool made ("has been booked", "is booked" while a change is under way); it calls a visit confirmed
  when no lookup shows it Confirmed; it asks the patient to confirm one specific change without having checked it; it says
  it will check something instead of checking.
- **Server-side conversations.** The browser holds only a conversation id. History, including tool results, stays in
  server memory (30 minutes idle, 4 hours at most), keyed by patient, so it can't be edited or read by anyone else.
  Nothing is written to the database. More than one API server will need a shared store (e.g. Redis).
- **Short ids for the model.** Tools show services, dentists and appointments as S1, D2, A3 instead of GUIDs,
  because small local models copy those reliably.
- **Limits** (all in the "Ai" settings): messages up to `MaxMessageLength` (1,000) characters, and the portal page uses the
  same number; at most `MaxModelCallsPerMessage` (6) model calls per message; `MessagesPerMinute` (12) per IP;
  `TimeoutSeconds` (120) per model call. Also fixed in code: 6 tool calls per model call, the last 40 messages sent.
- **The assistant can't claim what it didn't do.** If a reply says something was booked, moved or cancelled but no
  booking tool succeeded during that message, the model is told privately that nothing changed and asked to act or say
  so (up to 2 reminders). If it still claims success, the patient gets "Sorry, I couldn't complete that, so nothing has
  been changed" instead. The same reminder covers "Let me check..." with no lookup, and asking the patient for an ID.
  (Found in a real test: qwen2.5:7b once said "has been cancelled" without cancelling.)
- **Help for small models:** the services and dentists, with their short ids, are in the instructions from the first
  message; tools also accept a name ("Teeth Whitening", "Dr. Ahmed") instead of an id; find_open_times takes an optional
  `after` time (24-hour, for "afternoon" or a specific time; it says whether that exact time is open and echoes it in
  12-hour form, because the model once passed 13:15 for "2:15") and returns 4 times spread across the day per dentist,
  each with its 12-hour wording, and a count of
  the others (it read out all 17 when it got them), plus a ready-made offer that starts with the day and date ("Fri, Nov 20:
  12:00 PM, 1:30 PM, 3:00 PM or 4:30 PM with Dr. Sarah Ahmed"). list_my_appointments takes an optional day ("Friday",
  "tomorrow", a date) and the server picks the visits on it, because the model answered about Tuesday for "my Friday visit".
  find_open_times takes the day as the patient said it ("next Friday") and the server works out the date; a date on another
  weekday than the one the patient named is refused (the model passed 2026-11-22, a Sunday, for "next Friday").
  The instructions list the next 14 days with their dates, because the model
  once turned "Friday" into Tuesday. A Pending visit is described as waiting for the clinic, never "confirmed".
- **Logging:** every tool call is logged with its name and outcome (ok, or the tool's own error text). Arguments only at
  Debug level, because a booking note is the patient's own text.
- **No medical advice.** The prompt sends urgent symptoms to the clinic's phone or emergency services.
- **Doctor names** on the portal, in the assistant and in patient messages get "Dr." once (`DoctorNames`), even when staff
  typed the title into the name (first name "Dr."), which used to show "Dr. Dr. Sarah Ahmed".
- **Portal pages ignore the staff session.** The app used to check the staff sign-in on every page; with an expired staff
  token in the same browser, a patient opening the portal was sent to the staff sign-in ("Your session has expired").
- **Health data.** Free cloud tiers (e.g. Gemini's) may use prompts to improve their models: **test data only**. Real
  clinics need Ollama on our own servers, or a paid provider that signs a HIPAA business associate agreement. The
  API logs a warning when Gemini is used outside Development.

**Setup**
- Ollama: install it, run `ollama pull qwen2.5:7b` (about 5 GB of RAM), set `Ai:Provider` to `Ollama` (the Development default).
  `Ai:Ollama:ContextLength` (default 16384) overrides Ollama's 4096-token default, which is too small for the tools and a chat.
- Gemini: create a key in Google AI Studio, set the `Ai__Gemini__ApiKey` environment variable (never appsettings.json)
  and set `Ai:Provider` to `Gemini`. The default model is `gemini-2.5-flash`.

**Built**
- [x] API: `GET /portal/assistant` (on/off), `POST /portal/assistant/messages` (patient token, rate-limited); 503 when off or the model is down
- [x] Tools: list services, list dentists, find open times (one or all dentists), list my appointments, book, reschedule,
  cancel, list my invoices
- [x] Adapters: Ollama `/api/chat` and Gemini `generateContent` (function calling, thought signatures)
- [x] UI: "Ask us" in the portal menu (only when on), chat with suggested questions, a "See my visits" link after a change
- [x] Tests: a scripted model books end to end through the portal rules; it can't break the notice rule or touch another
  patient's visit; off, down, staff and anonymous callers; conversations can't cross patients; adapter JSON both ways;
  provider selection

**Not done yet**
- Builds; all tests pass (262 unit, 134 integration). Tried end to end on 2026-11-13 against the running API with Ollama
  and qwen2.5:7b on the dev machine: questions, afternoon times, booking, "when is my next visit" and cancelling all work
  (about 1–6 s a reply on the GPU; the first call after loading takes about a minute). On 2026-11-14 the chat was clicked
  through in the browser: "cleaning next Friday afternoon" → 4 times on Fri, Nov 20 → "can I have 2:15 instead?" → one
  confirmation question → "yes please" → booked (Pending), and "what time is my Friday visit?" in a new chat found it.
- Gemini hasn't been tried (no key yet). The model sometimes leaves the date out of its own wording.
- Later: OpenAI and Claude adapters; a per-clinic on/off switch; a staff view of chats; the assistant for visitors
  who aren't signed in (FAQ only); Bengali.

### Phases 7–10
Scoped in detail when each phase starts. Key dependencies:
- The AI receptionist (6) builds on stable booking/reschedule APIs (1) and patient lookup by phone (1.5).
- Voice (7) builds on chat (6) and telephony (3).

## 9. Out of scope (for now)
Clinical charting / odontograms, imaging (X-ray), e-prescriptions, and electronic claims
submission (EDI 837). These may come after Phase 9.
