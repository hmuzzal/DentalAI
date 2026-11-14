# DentalAI: notes for contributors and AI assistants

Multi-tenant dental clinic SaaS, US-first (Bangladesh localisation later). ASP.NET Core (.NET 10) API with
Clean Architecture, Angular 20 client in `DentalAI.Web/`.

- **Vision, roadmap, status and phase checklists: `product.md`.** Tick items off and update §6 "Roadmap",
  §7 "Current status" and the phase's checklist in §8 when something lands.

## Commands

```bash
# backend (from the repo root)
dotnet build DentalAI.sln
dotnet test DentalAI.sln             # integration tests need SQL Server (see below)
dotnet run --project DentalAI.Api    # migrates + seeds on start; Swagger at /swagger
dotnet ef migrations add <Name> --project DentalAI.Infrastructure --startup-project DentalAI.Api --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project DentalAI.Infrastructure --startup-project DentalAI.Api

# frontend (from DentalAI.Web/)
pnpm install --frozen-lockfile
pnpm start                           # http://localhost:4200
pnpm build
pnpm exec ng test --watch=false --browsers=ChromeHeadless
```

`DentalAI.Api.IntegrationTests` host the real API against a throwaway SQL Server database (the booking lock uses
`sp_getapplock`, so no in-memory provider). Set `DENTALAI_TEST_SQLSERVER` to the server (default `.`, Windows
authentication), e.g. `(localdb)\MSSQLLocalDB`.

CI (`.github/workflows/ci.yml`) runs the backend and frontend build + tests on every PR and push to `master`.

## Layout

| Project | Holds |
| :--- | :--- |
| `DentalAI.Domain` | Entities and domain rules |
| `DentalAI.Application` | Services, DTOs, validators (FluentValidation), interfaces, hand-written mappings (no AutoMapper) |
| `DentalAI.Infrastructure` | `Persistence` (DbContext, EF configurations, migrations, seeder, repositories), `Security` (JWT, refresh tokens), `Notifications` (SMTP/Twilio senders), `Ai` (Ollama/Gemini chat models) |
| `DentalAI.Api` | Controllers, middleware, `Program.cs` |
| `DentalAI.Application.Tests` | xUnit unit tests (scheduling engine, DST, validators, …) |
| `DentalAI.Api.IntegrationTests` | xUnit + `WebApplicationFactory` against SQL Server |
| `DentalAI.Web` | Angular 20: standalone components, signals, ng-zorro-antd, date-fns, pnpm |

## Rules

- **Tenant isolation:** every clinic-owned row carries `ClinicId`, and every read and write is checked against the
  signed-in user's clinic. Requests never take a `clinicId` from the client for clinic-owned data.
- **UTC in storage, clinic time in the UI:** store instants as UTC; schedules and slots are computed in the clinic's
  IANA time zone. Test DST edges (America/New_York, March and November) when touching time logic.
- **The scheduling engine is the single source of truth** for availability. Staff UI, patient portal and the AI
  receptionist all book through the same API and validation.
- **Provider abstraction:** SMS, email, payments, AI and voice sit behind interfaces; no provider types in Domain or
  Application.
- **i18n:** user-facing Angular strings go through `i18n` / `$localize`.
- **Phones** are stored as E.164 (`PhoneNumberRules`).
- **Secrets:** never commit real credentials or API keys. Local values go in user secrets or
  `appsettings.Development.json` placeholders.
- **Migrations** carry `[Migration]` + `[DbContext]` attributes (a `.Designer.cs`) and keep the model snapshot in sync.
  Don't add new one-off schema patches to `Program.cs`; use a migration.

## Workflow

One vertical slice per branch (`feature/<slice>`): entity → migration → service → API → Angular screen → tests.
Definition of done: CI green, tests for the new domain rules, `product.md` updated.

### Autonomous runs (AI assistants)

- **Picking work:** take the first unchecked item of the current phase in `product.md` §8, then the open
  (`- [ ]`) items under §7 "Technical debt / known issues", then the next ⬜ phase in §6. Phases 7–10 are scoped
  when they start: plan them and ask for approval before building.
- **Open decisions are not yours:** if a slice needs a product decision, a provider account or credentials (e.g.
  Stripe for 2c, Twilio, a Gemini key), stop and ask. Never pick a default silently.
- **Start from the latest `master`:** before implementing anything, `git fetch` and branch from (or rebase onto)
  `origin/master`.
- **Loop per slice:** plan → build the slice → `dotnet build` + `dotnet test` + `pnpm build` + `ng test` →
  self-review → update `product.md` → open a PR against `master`.
- **Merge when green:** before merging, fetch and rebase onto the latest `origin/master` again, resolve any
  conflicts on the branch, re-run every check above, and wait for CI to pass. Then squash-merge the PR and delete the
  branch. Never push directly to `master`, never merge with failing or skipped checks, and never force-push `master`.
- **Parallel work:** independent slices can run at the same time, each in its own worktree and branch. Every
  migration rewrites `ApplicationDbContextModelSnapshot`, so build only one migration-adding slice at a time. If
  `master` gained a migration, remove yours, rebase, and regenerate it instead of hand-merging the snapshot.
- **Stay in scope:** new ideas go into the relevant "Later" note in `product.md`, not into the current slice.

### Sub-agents (fullstack-dev-kit)

Use them on every slice, not only when asked:

- **Before building:** a slice that touches more than one layer (entity, API, Angular) goes through
  `fullstack-planner` first; use `dotnet-architect` when choosing a backend approach.
- **While building:** hand Angular screens to `angular-specialist`, and model changes to `ef-migration-helper`.
- **Before the PR:** run `code-reviewer` and `test-writer` in parallel. Also run `security-reviewer` for any change
  touching patient data, sign-in (staff or portal), the AI receptionist's tools, invoices/payments or `ClinicId`
  scoping. Fix what they find before opening the PR.
- **Red build or tests:** use `build-error-resolver`.
