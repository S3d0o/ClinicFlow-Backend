# 09: Migration from V1

V2 is built in the same codebase and on the same database as V1. There is no big-bang rewrite: each phase adds tables and endpoints, migrates the data it needs, and moves frontend screens to `/api/v2`. This document covers the data, the API and the frontend, and maps every open V1 issue to where V2 resolves it.

## 1. Principles

- **Additive schema first.** New tables and nullable columns ship before any column is dropped. Old columns are removed only after the code no longer reads them (expand → migrate → contract).
- **Every data migration is a script with a dry run.** It is idempotent, it runs on an anonymised staging copy first, and it produces a reconciliation report (row counts, sums, checksums) that someone signs off.
- **V1 keeps working** until the frontend has moved ([ADR 0005](adr/0005-api-v2-versioning.md)).
- **No data is lost.** Free text that can't be mapped to a structure is kept verbatim and flagged "migrated from V1".

## 2. Data migration

Steps run in this order. The phase column says when.

| # | Step | Phase | Details |
|---|---|---|---|
| 1 | Create the organization | P0 | One `Organization` row from configuration (name, registration numbers, default time zone `Africa/Cairo`) |
| 2 | Backfill `OrganizationId` | P0 | Added (nullable) to every organization-owned V1 table, filled with the one id, then made non-null |
| 3 | Create branches | P1 | Extract distinct `(ClinicCity, ClinicAddress)` from approved doctors into a **branch mapping sheet** (CSV). The clinic manager edits it: merges spelling variants, names branches, assigns codes. The script reads the approved sheet. Doctors with no address go to the default branch `MAIN` |
| 4 | Staff members | P1 | Users with role `Admin` → `StaffMember` with `Admin` @ Organization. Approved doctors → `StaffMember` `Active` with `Doctor` @ their mapped branch, `DoctorProfile.IsPublic = true`. Unapproved doctors → a `DoctorApplication` in `Applied`, flagged "migrated from V1, no CV", so the manager reviews them like any applicant (they never appeared publicly); their V1 profile data fills the application. Suspended doctors → `StaffMember` `Inactive` |
| 4a | Users: phone and language | P1 | `ApplicationUser.PhoneNumber` normalised to E.164 (numbers that fail are listed for the desk to fix), `PhoneNumberConfirmed = false` (verified by SMS at the first V2 login); `PreferredLanguage = ar`; `NotificationChannel = Both` for users with a confirmed email, else `Sms` |
| 5 | Services and prices | P1 | One service per specialty, "Consultation – <Specialty>", 30 min default. One `PriceListItem` per doctor from `ConsultationFee` (doctor-specific price, valid from the migration date) |
| 6 | Schedules and slots | P2 | `DoctorSchedule.BranchId` from the doctor's branch. `AppointmentSlot.BranchId`, `StartsAtUtc`, `EndsAtUtc` computed with the branch zone; `Capacity = 1`, `BookedCount` from current status |
| 7 | Patient records | P2 | One `PatientRecord` per `PatientProfile`; MRNs assigned in `CreatedAt` order. Emergency contact and blood type move to the record. `Allergies` and `ChronicConditions` text → one `Allergy` / `Condition` entry each, flagged "migrated" for review at the next visit |
| 8 | Appointments | P2 | `BranchId` from the slot; `ServiceId` = the doctor's consultation service; `PatientRecordId` from step 7; `Source = Online`. Statuses unchanged (`Confirmed`, `Cancelled`, `Completed` are valid V2 values) |
| 9 | Reviews and notifications | P2 | `OrganizationId`; `Review.BranchId` from the appointment |
| 10 | Doctor notes → encounters | P4 | For each appointment with `DoctorNotes`: an `Encounter` with `Plan` = the notes, `Status = Signed`, `SignedAt` = appointment completion (or booking) time, author = the doctor, flagged "migrated from V1". `DoctorNotes` is then dropped in a later release |
| 11 | Drop retired columns | After P4 | `DoctorProfile.ClinicAddress`, `ClinicCity`, `ConsultationFee`, `IsApprovedByAdmin`; `PatientProfile` clinical columns; `Appointment.DoctorNotes`, `PatientProfileId` |

**Reconciliation checks** (each must match before sign-off): approved and suspended doctors = staff doctors, and unapproved doctors = migrated applications; sum of `ConsultationFee` = sum of migrated prices; patients = patient records; appointments per status unchanged; notes count = migrated encounters; no row without `OrganizationId`.

**Rollback:** every step before 11 is reversible because nothing is dropped; the release before a migration can run against the migrated database (new columns are nullable or defaulted until the code depends on them).

## 3. API migration

| Stage | When | What |
|---|---|---|
| Freeze | Start of P0 | V1 endpoints get bug and security fixes only, recorded as contract 3.x. `API_CONTRACT.md` §9 lists the deprecations below with dates |
| Coexist | P1–P5 | Each phase ships `/api/v2` endpoints (contract 4.x). V1 endpoints keep working, backed by V2 services where possible |
| Deprecate | When the frontend moves a screen | The V1 endpoint returns a `Deprecation` header and a `Sunset` date |
| Remove | V2 general availability (end of P5), or earlier per endpoint by agreement | V1 controllers deleted; contract 4.x only |

V1 sign-up requires an email and stays as it is while it exists. Accounts created through V2 without an email can't log in through V1's email login; the frontend uses V2 sign-in for them from P1.

Endpoints removed **earlier** because their concept disappears:

| V1 endpoint | Replacement | Removed when |
|---|---|---|
| `POST /api/auth/register/doctor` | Doctor applications (`POST /api/v2/doctor-applications`) | End of P1 |
| `GET /api/admin/doctors/pending`, `POST /api/admin/doctors/{id}/approve` | Recruitment (`/api/v2/recruitment/applications`, interview / accept) | End of P1 |
| `POST /api/admin/doctors/{id}/suspend` | `POST /api/v2/staff/{id}/deactivate` | End of P1 |
| `/api/Specialty` write routes (already deprecated) | `/api/admin/specialties`, then `/api/v2/...` | P0 |
| `PUT /api/appointments/{id}/notes` | Encounter endpoints | End of P4 |

## 4. Frontend migration

The frontend lives in a separate repository; each phase has a frontend track with the same exit criteria.

| Phase | Frontend work |
|---|---|
| P0 | Timestamps with `Z`; new appointment statuses displayed (ProblemDetails and paged lists already handled in V1) |
| P1 | Arabic/English switch with RTL; sign-up by mobile with the SMS code step; login by phone or email; profile language and channels; staff app shell: login with MFA, `GET /api/v2/me`, permission-driven menus; admin screens (staff, roles, branches, rooms, services, prices); **doctor application form and "My application"; recruitment screens** (list, detail, CV download, interview / accept / reject with email preview); remove the V1 doctor sign-up and approval screens |
| P2 | Front desk (patient search/registration, calendar, booking on behalf, walk-in, check-in, queue board, waiting-room display); manager scheduling (schedules, leave, worklist); patient portal booking by branch/service with prices; notification channel choice; **admin dashboard** (KPI tiles, charts 1, 2, 4) |
| P3 | Checkout at the desk (amount due, cash/card, receipt), invoices, refunds, approvals inbox, cash sessions; optional Paymob deposit in the portal; dashboard chart 3 (revenue) |
| P4 | Nurse triage screen; doctor workspace (queue, history, encounter editor, prescriptions, orders, signing); patient visit summaries |
| P5 | Payers, plans, policies, eligibility, pre-auth, claims workspace |
| P6 | Reports and exports |
| P7 | Chatbot panel in the patient portal |

**Training and rollout:** a pilot branch first for each operational phase (P2–P5), with super-users per role, a training sandbox seeded with demo data, and a two-week parallel run (paper backup) before the next branch.

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
| M6 | No brute-force protection on login | ✅ `d3b6511`, `b4a2a47` (lockout, per-IP auth rate limit) | Rate limits on booking, public search and SMS-code endpoints (§3.4 of 06) | P0–P1 |
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
| NL12 | `EmailSettings` not validated | ✅ `e5cc0db`, `b4a2a47` | Validate SMS and Paymob settings the same way | P1, P3 |
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
| — | AutoMapper logs that production use needs a paid licence | ⏸ (left open at V1 sign-off) | Decide: licence, pin a free version, or explicit mapping (preferred, [06 §2.1](06_TECHNICAL_DESIGN.md#21-design-principles)) | P0 |
| Duplicated logic | Three cancel flows, two review routes, double specialty CRUD, validator copies | 🟡 | Replaced by the state machine, V2 routes and shared helpers | P0–P2 |
