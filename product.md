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
| 1.5 | US readiness: E.164 phones, clinic country, patient address and SMS consent, insurance | ✅ Done 2026-11-01 |
| 2 | Billing, invoices and payments (provider-independent core; online payments via the clinic's own Stripe) | 🟡 2a done 2026-11-02 (billing core API, currency setting); 2b UI and 2c online payments next |
| 3 | Email and SMS reminders (Twilio / SendGrid, background jobs) | ⬜ |
| 4 | Reports and analytics | ⬜ (a basic dashboard exists) |
| 5 | Patient portal (self-booking) | ⬜ |
| 6 | AI receptionist: chat (Claude, tool use against the scheduling API) | ⬜ |
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
- [ ] Phones saved before 2026-11-01 are not E.164 (e.g. the demo clinic's `+1-555-0101`). They are normalised the
  next time each record is saved; before SMS (Phase 3) add a one-off backfill that normalises them or flags the ones that fail.
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
- [ ] **Invite emails** (Phase 3): send the invite link by email instead of the admin copying it. Same for "Forgot password",
  which can reuse the invite-token mechanism.
- [ ] Access tokens stay valid until they expire (60 minutes) after someone is deactivated or their clinic is deleted.
  The next page load ends the session (`/auth/me` returns 401), but API calls with the old token work until expiry.
  If that window matters, check the user's status per request (cached) or shorten the token lifetime.
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

**2b. Billing UI**
- Billing nav group: **Invoices** (filter by status, patient and dates; totals outstanding), **invoice detail** (lines,
  payments, record payment, refund, void), "Create invoice" from a completed appointment and from a patient, patient
  balance on the patient record.
- **Printable invoice** (print stylesheet → browser "Save as PDF"). Server-side PDF (QuestPDF) later if needed.

**2c. Online payments (needs a Stripe account to develop and test)**
- `IPaymentGateway` (create a payment link or checkout session, verify a webhook, refund). `StripeGateway` uses the
  **clinic's own** restricted API key and webhook secret, stored encrypted (ASP.NET Data Protection) in clinic billing
  settings; cards are entered only on Stripe's hosted Checkout (no card data touches DentalAI).
- "Send payment link" on an invoice; the webhook records the payment idempotently (by Stripe event ID).
- Later, with a company in a supported country: Stripe Connect (clinics onboard through Stripe), Stripe Terminal for
  in-clinic cards, and DentalAI's own subscription billing.

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
