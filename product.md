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
| 1.5 | US readiness: E.164 phones, insurance profile, i18n scaffold | ⬜ Not started |
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

**Build:** API ✅ builds · Angular ✅ builds · Tests ✅ 4/4 pass (SchedulingEngine only).
**Git:** a single commit (`first commmit`). All Phase 1 work is **uncommitted** in the working tree.

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
1. **No booking UI.** `time-slot-selector` exists but no screen uses it, and the Angular
   appointments service has no `create` call. Receptionists cannot book from the web app.
2. **No reschedule.** There is no endpoint to move an appointment to a new slot.
3. **Double-booking race.** Validation and insert are not atomic, so two concurrent
   requests can book the same slot. Needs a serializable transaction or an app lock per doctor.
4. **Times shown in browser time, not clinic time.** The `date` pipes use the browser's
   zone, so a New York clinic viewed from another zone shows wrong times.
5. **Clinic time-zone picker.** The clinic form has no time-zone selector (it should be a
   dropdown of IANA zones, defaulting to US zones). The validator does not check that the zone ID is real.
6. **DST edge cases.** `ToUtc` throws on local times that don't exist during the
   spring-forward hour, and repeated fall-back times are ambiguous. Needs handling and tests.
7. **Dashboard revenue grouped by UTC date** instead of clinic-local date.
8. **Slot interval hard-coded at 15 minutes.** It should be a clinic setting.
9. **Test coverage.** Only 4 engine tests. Missing: DST, multiple time zones, service and
   appointment validation, tenant isolation.

### Technical debt / known issues (noted 2026-11-01, open until fixed)
- [ ] **Security: vulnerable packages.** AutoMapper 13.0.1 (high, GHSA-rvv3-g6hj-g44x) and
  System.Security.Cryptography.Pkcs 9.0.4 (high GHSA-846f-f764-76mx, moderate GHSA-2fmv-rv2c-59x7). Upgrade.
- [ ] **Security: JWT secret in `appsettings.json`.** Move it to user-secrets (dev) and
  environment variables or a key vault (prod), then rotate the key.
- [ ] **`Program.cs` is ~980 lines of one-off DB patches.** The startup `AlignLegacy*Schema`
  raw-SQL methods should become migrations, or be deleted once all databases are migrated.
- [x] **Build/cache folders in git.** `.dotnet-cli`, `.pnpm-store`, `.nuget`, `.buildtmp`,
  `.appdata` and `.vs` show up in git. Add them to `.gitignore` and untrack them. ✅ Done 2026-11-01.
- [ ] No Angular unit tests and no API integration tests.

## 8. Phase plans

### Phase 1: finish the scheduling engine (next)
**Backend**
- [ ] Atomic booking: run validate + insert in a serializable transaction (or `sp_getapplock` keyed on DoctorId); return 409 on conflict
- [ ] `POST /appointments/{id}/reschedule` (new start time, re-validated by the engine, excluding the appointment itself)
- [ ] DST-safe `ToUtc`: skip non-existent local times, pick the first instance of ambiguous ones
- [ ] Validate the clinic `TimeZone` as a real zone ID; endpoint `GET /lookups/time-zones` (US zones first)
- [ ] Return the clinic time-zone ID with the slot, calendar and appointment DTOs
- [ ] Clinic setting `SlotIntervalMinutes` (default 15)
- [ ] Dashboard: group revenue by clinic-local date
- [ ] Tests: DST (America/New_York, March and November), multiple zones, overlap, leave, reschedule

**Frontend**
- [ ] Booking flow: patient → service → doctor → date → `time-slot-selector` → confirm
- [ ] Reschedule and cancel actions from the calendar and appointments list
- [ ] Time-zone-aware formatting: a shared pipe or helper that formats in the clinic's zone
- [ ] Time-zone dropdown on the clinic form
- [ ] i18n scaffold (Angular `$localize`); English as the source locale

**Housekeeping**
- [x] `.gitignore` the generated folders, then commit Phase 0 cleanup and Phase 1 separately

### UI track (runs alongside Phase 1)
- [x] 1. Foundation: design tokens (`styles.scss`), ng-zorro teal theme, shared `app-page-header`,
      `app-page-state` (loading/empty/error), `remoteData()` loader
- [x] 2. App shell: grouped nav, clinic name and time zone in the header, user menu, collapsible sidebar, mobile drawer
- [x] 3. Fixes: menu icons, dashboard developer notes removed, dashboard uses the scheduling stats and revenue chart,
      route-based tab titles, favicon, global HTTP error toasts, seed login shown in development only
- [ ] "+ New appointment" header button (arrives with the booking flow, step 6)
- [ ] 4. Management pages: search, paging, add/edit side panel (Doctors, Patients, Services, Clinics)
- [ ] 5. Merge Appointments and Calendar; rebuild Doctor schedules as a weekly grid
- [ ] 6. Booking flow (= Phase 1 gap 1)

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
