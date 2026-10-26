# 09: Moving from V1 to V2

V2 is a **separate product line** ([ADR 0007](adr/0007-v2-separate-product-line.md)). It is built on the `v2` branch, started from V1's code, and runs on **its own database** (`ClinicFlowV2`). V1 stays on `master` with its own database and is never changed by V2 work. The owner decides if and when V2 replaces V1. This document covers:
- the **import tool** that copies V1 data into V2 at that point
- what happens to the V1 API and frontend code inherited on the `v2` branch
- where each known V1 issue is resolved

## 1. Principles

- **V1 is read-only to V2.** No V2 migration, job or endpoint touches V1's database. The import tool opens V1 with a read-only login.
- **V2 owns its schema.** V2 migrations run only against the V2 database, so V2 may rename, reshape or drop V1-era columns whenever its own code stops using them. There is no expand → migrate → contract against live V1 data.
- **The import is a tool, built phase by phase.** Each phase that adds a V2 concept with a V1 counterpart adds its import step and a test for it. The tool is idempotent, has a dry-run mode, runs on an anonymised copy first, and produces a reconciliation report (row counts, sums, checksums) that the owner signs off.
- **No data is lost.** Free text that can't be mapped to a structure is kept verbatim and flagged "migrated from V1".
- **V1 is the fallback.** Until the owner retires it, V1 stays deployable with its data as it was at the cut-over.

## 2. Importing V1 data

Run only when the owner decides to switch. The **Built in** column says which phase adds the step to the tool. The cut-over:
1. Stop writes in V1 (maintenance page).
2. Back up both databases.
3. Run the import in dry-run mode, then for real.
4. Check the reconciliation report.
5. Switch DNS to V2.

| # | Step | Built in | Details |
|---|---|---|---|
| 1 | Create the organization | P0 | One `Organization` row from configuration (name, registration numbers, default time zone `Africa/Cairo`) |
| 2 | Organization on every row | P0 | Every imported organization-owned row gets the one `OrganizationId` |
| 3 | Create branches | P1 | Extract distinct `(ClinicCity, ClinicAddress)` from approved doctors into a **branch mapping sheet** (CSV). The clinic manager edits it: merges spelling variants, names branches, assigns codes. The tool reads the approved sheet. Doctors with no address go to the default branch `MAIN` |
| 4 | Staff members | P1 | Users with role `Admin` → `StaffMember` with `Admin` @ Organization. Approved doctors → `StaffMember` `Active` with `Doctor` @ their mapped branch, `DoctorProfile.IsPublic = true`. Unapproved doctors → a `DoctorApplication` in `Applied`, flagged "migrated from V1, no CV", so the manager reviews them like any applicant (they never appeared publicly); their V1 profile data fills the application. Suspended doctors → `StaffMember` `Inactive` |
| 4a | Users: identity, phone and language | P1 | Users are copied with their Identity data (password hashes, so V1 passwords keep working; refresh tokens are **not** copied, so everyone logs in again). `PhoneNumber` normalised to E.164 (numbers that fail are listed for the desk to fix), `PhoneNumberConfirmed = false` (verified by a one-time code at the first V2 login); `PreferredLanguage = ar`; `NotificationChannel = Both` for users with a confirmed email, else `Phone` |
| 5 | Services and prices | P1 | One service per specialty, "Consultation – <Specialty>", 30 min default. One `PriceListItem` per doctor from `ConsultationFee` (doctor-specific price, valid from the import date) |
| 6 | Schedules and slots | P2 | `DoctorSchedule.BranchId` from the doctor's branch. Future `AppointmentSlot`s with `BranchId`, `StartsAtUtc`, `EndsAtUtc` computed with the branch zone; `Capacity = 1`, `BookedCount` from current status. Past slots are imported only when an appointment refers to them |
| 7 | Patient records | P2 | One `PatientRecord` per `PatientProfile`; MRNs assigned in `CreatedAt` order. Emergency contact and blood type move to the record. `Allergies` and `ChronicConditions` text → one `Allergy` / `Condition` entry each, flagged "migrated" for review at the next visit |
| 8 | Appointments | P2 | `BranchId` from the slot; `ServiceId` = the doctor's consultation service; `PatientRecordId` from step 7; `Source = Online`. Statuses unchanged (`Confirmed`, `Cancelled`, `Completed` are valid V2 values) |
| 9 | Reviews and notifications | P2 | `OrganizationId`; `Review.BranchId` from the appointment |
| 10 | Doctor notes → encounters | P4 | For each appointment with `DoctorNotes`: an `Encounter` with `Plan` = the notes, `Status = Signed`, `SignedAt` = appointment completion (or booking) time, author = the doctor, flagged "migrated from V1" |

V1-era columns that V2 no longer uses are dropped by ordinary V2 migrations once V2's own code stops reading them: `DoctorProfile.ClinicAddress`, `ClinicCity`, `ConsultationFee`, `IsApprovedByAdmin`; `PatientProfile` clinical columns; `Appointment.DoctorNotes`, `PatientProfileId`. The import tool reads them from V1, so dropping them in V2 loses nothing.

**Reconciliation checks** (each must match before sign-off): approved and suspended doctors = staff doctors, and unapproved doctors = migrated applications; sum of `ConsultationFee` = sum of migrated prices; users and patients = imported users and patient records; appointments per status unchanged; notes count = migrated encounters; no row without `OrganizationId`.

**Rollback:** V1 was never changed. If the reconciliation fails or V2 misbehaves after the switch, point DNS back to V1, which is complete up to the moment writes stopped.

## 3. The API on the `v2` branch

- **V1's API** lives on `master` and is served by the V1 deployment. Its contract is `docs/API_CONTRACT.md` 3.x on `master`. V2 work never changes it.
- **V2's API** is under `/api/v2` (contract 4.x, in `docs/API_CONTRACT.md` on the `v2` branch). Each phase adds its endpoints.
- **Inherited unversioned routes** on the `v2` branch are a starting point, not a contract:
  - they serve only the V2 frontend while its screens are rebuilt
  - they may change freely and need no `Deprecation`/`Sunset` headers
  - each one is deleted from `v2` once the V2 frontend no longer calls it, and all are gone by V2 GA

  Routes whose concept disappears go first:

| Inherited route | V2 replacement | Deleted from `v2` |
|---|---|---|
| `POST /api/auth/register/doctor` | Doctor applications (`POST /api/v2/doctor-applications`) | End of P1 |
| `GET /api/admin/doctors/pending`, `POST /api/admin/doctors/{id}/approve` | Recruitment (`/api/v2/recruitment/applications`, interview / accept) | End of P1 |
| `POST /api/admin/doctors/{id}/suspend` | `POST /api/v2/staff/{id}/deactivate` | End of P1 |
| `/api/Specialty` write routes | `/api/v2/...` specialty management | P1 |
| `POST /api/auth/register`, `POST /api/auth/login` | `/api/v2/auth/register`, `/api/v2/auth/login` (phone or email) | End of P1 |
| `PUT /api/appointments/{id}/notes` | Encounter endpoints | End of P4 |

## 4. The V2 frontend

The frontend repository follows the same split: `main` is V1, and `v2` is the V2 frontend, started from V1's code. Each phase has a frontend track with the same exit criteria.

| Phase | Frontend work |
|---|---|
| P0 | Point at the V2 API and database environment; timestamps with `Z`; new appointment statuses displayed (ProblemDetails and paged lists already handled in V1 code) |
| P1 | Arabic/English switch with RTL; sign-up by mobile with the one-time code step; login by phone or email; profile language and channels; staff app shell: login with MFA, `GET /api/v2/me`, permission-driven menus; admin screens (staff, roles, branches, rooms, services, prices); **doctor application form and "My application"; recruitment screens** (list, detail, CV download, interview / accept / reject with email preview); remove the inherited doctor sign-up and approval screens |
| P2 | Front desk (patient search/registration, calendar, booking on behalf, walk-in, check-in, queue board, waiting-room display); manager scheduling (schedules, leave, worklist); patient portal booking by branch/service with prices; notification channel choice; **admin dashboard** (KPI tiles, charts 1, 2, 4) |
| P3 | Checkout at the desk (amount due, cash/card, receipt), invoices, refunds, approvals inbox, cash sessions; optional Paymob deposit in the portal; dashboard chart 3 (revenue) |
| P4 | Nurse triage screen; doctor workspace (queue, history, encounter editor, prescriptions, orders, signing); patient visit summaries |
| P5 | Payers, plans, policies, eligibility, pre-auth, claims workspace |
| P6 | Reports and exports |
| P7 | Chatbot panel in the patient portal |

**Trying V2 and rolling it out:**
- **Before the owner decides,** each phase's exit criteria are checked in a V2 staging environment seeded with V2's demo data.
- **If the owner decides to pilot,** V2 runs for one branch with real data (imported, §2), with super-users per role and a two-week parallel run with a paper backup, while V1 keeps serving the rest.

## 5. Where each V1 issue is resolved

From [REVIEW.md](../REVIEW.md). **Status 2026-10-22:** V1 was finished on 2026-10-18 (contract 3.1.2) and fixed most of the items this table originally assigned to P0. The **Status** column says what V1 already did; **V2 work** is what is left. A phase of "—" means nothing is left for V2.

| REVIEW item | Problem | Status in V1 | V2 work | Phase |
|---|---|---|---|---|
| H4 | Editing/deleting schedules with bookings fails with 500 and half-applies | ✅ `717f470` | Per-branch shifts keep the same rule (FR-SCH-03, FR-SCH-10) | P2 |
| H6 | Appointment paging defaults and validation | ✅ `757cf7e` | One paging rule for every V2 list ([06 §10](06_TECHNICAL_DESIGN.md#10-data-access-and-performance)) | — |
| H8 | Slots run out after 14 days | ✅ `8321b0f` (`SlotGenerationJob`) | Per-branch, 28-day window (FR-SCH-02) | P2 |
| M1 | Approval not enforced | ✅ `d63cdba` (gate on public visibility) | The approval concept is replaced by doctor applications (FR-REC) | P1 |
| M2 | Local slot times compared to UTC | ✅ `8321b0f` for one configured clinic zone (`ClinicNow`) | Branch time zones, `StartsAtUtc`, `IClock` ([ADR 0003](adr/0003-branch-time-zones.md)) | P0 |
| M3 | Expected errors return 500 | ✅ `da53e0c` | — | — |
| M5 | No global exception handler | ✅ `757cf7e` | — | — |
| M6 | No brute-force protection on login | ✅ `d3b6511`, `b4a2a47` (lockout, per-IP auth rate limit) | Rate limits on booking, public search and one-time-code endpoints (§3.4 of 06) | P0–P1 |
| M12 | Docker healthcheck always fails | ✅ `/health` | Split into `/health/live` and `/health/ready` | P0 |
| M13 | Missing validators, inconsistent phone rules | ✅ `757cf7e` | One shared phone (E.164) and password rule set for every V2 validator | P0 (V2 per phase) |
| M14 | Incomplete appointment state rules | ✅ `38d8a85`, `0bc5c78` (rules in the service) | Move the rules into the entity's state machine ([ADR 0004](adr/0004-appointment-state-machine.md)) | P0 |
| M15 | Doctor profile update forces a fee | ✅ `05cff27` | Fees move to the price list | P1 |
| L6 | Specialties can't be reactivated | ✅ `757cf7e` | — | — |
| L7 | Logout needs a valid access token | ✅ `d3b6511`, `b4a2a47` | — | — |
| L8 | Rating recalculation in a separate save | ✅ `3a2c391`, `b4a2a47` | — | — |
| NM3 | Email sent inline | 🟡 `d3b6511` (timeout added; still inline) | Outbox ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)) | P0 |
| NM5 | No fallback authorization policy | ✅ `757cf7e` (+ explicit-attribute test) | Allowlist of anonymous endpoints in the test | P0 |
| NM6 | App connects as `sa` | ✅ `e5cc0db`, `b4a2a47` (`clinicflow_app`, `db_owner`) | A narrower app login and a separate deploy login once migrations leave startup ([08 §3](08_SECURITY_PRIVACY_COMPLIANCE.md#3-data-protection)) | P0 |
| NL1 | Shared read/write repository methods | ✅ `3a2c391`, `bea10ad` | Keep reads and writes split in new modules | — |
| NL2 | Unbounded lists | ✅ `757cf7e` | `X-Total-Count` on every V2 list | — |
| NL3 | Doctor search can't use an index | 🟡 `3a2c391` (`ToLower` removed; leading-wildcard `LIKE` still scans) | Normalised search columns ([06 §10](06_TECHNICAL_DESIGN.md#10-data-access-and-performance)) | P2 |
| NL5 | Overview stats run 6 counts | ✅ `3a2c391` (kept on purpose for the overview) | Dashboard queries per chart (FR-RPT-11); reporting tables later | P2, P6 |
| NL6 | Reminder job round trips | ⏸ Open (kept on purpose) | Outbox batch delivery, one SMTP connection per batch | P0 |
| NL9 | Three 400 shapes, empty error bodies | ✅ `757cf7e` | camelCase validation keys in `/api/v2` | — |
| NL10 | Controller routes behaviour by first role claim | ⏸ Open | Permissions + one service method per action | P0 |
| NL11 | Missing `date` returns `[]` | ✅ `757cf7e` | — | — |
| NL12 | `EmailSettings` not validated | ✅ `e5cc0db`, `b4a2a47` | Validate WhatsApp, SMS and Paymob settings the same way (when the channel is enabled) | P1, P3 |
| NL14 | No retry on transient SQL faults | ⏸ Open (medium fix risk) | `EnableRetryOnFailure` + execution strategy in `IUnitOfWork` | P0 |
| NL17 | `Email!` / `Name!` uses | ✅ `d3b6511`, `e5cc0db`, `b4a2a47` | `ICurrentUser` | — |
| NL18 | Legacy `JwtSecurityTokenHandler` | 🟡 `d3b6511` (`MimeMessage` disposed; handler kept) | Switch to `JsonWebTokenHandler` with V2 tokens, keeping claim names | P1 |
| NL21 | `UnitOfWork` creates repositories | ⏸ Open (deliberate) | Kept by decision; revisited only if decoration is needed | — |
| §4.1 | "Now" read in many places | 🟡 `IDateTimeProvider` used in business rules | `IClock` everywhere + a test that forbids `DateTime.UtcNow` | P0 |
| §4.3 | No appointment state machine | ⏸ | [ADR 0004](adr/0004-appointment-state-machine.md) | P0 |
| §4.4 | Notifications hand-called in services | ⏸ (`TryNotifyAsync` helpers) | Domain events + outbox | P0 |
| §4.5 | Fragile slot materialisation | 🟡 rolling window in V1 | Scheduling 2.0 | P2 |
| §4.6 | Layering leaks | ⏸ | `ITransaction` abstraction; drop `Services → Persistence`; architecture tests ([ADR 0006](adr/0006-clean-architecture-modular-monolith.md)) | P0 |
| §4.7 | Role strings and hardcoded config | 🟡 | Role and permission constants; settings service | P0–P1 |
| §4.9 | Startup migrations and seeding | 🟡 seeding gated by `Seeding:DemoData`; migrations still at startup | Migration bundle in the deploy step | P0 |
| §4.10 | Little test coverage, no CI | 🟡 tests added through V1; no CI workflow | CI pipeline and architecture tests ([06 §12](06_TECHNICAL_DESIGN.md#12-testing-strategy)) | P0 |
| — | AutoMapper logs that production use needs a paid licence | ⏸ (left open at V1 sign-off) | Deferred (Q17): licence, pin a free version, or explicit mapping ([06 §2.1](06_TECHNICAL_DESIGN.md#21-design-principles)); decided before V2 goes to production | Later |
| Duplicated logic | Three cancel flows, two review routes, double specialty CRUD, validator copies | 🟡 | Replaced by the state machine, V2 routes and shared helpers | P0–P2 |
