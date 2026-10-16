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
| 4 | Staff members | P1 | Users with role `Admin` → `StaffMember` with `Admin` @ Organization. Approved doctors → `StaffMember` `Active` with `Doctor` @ their mapped branch, `DoctorProfile.IsPublic = true`. Unapproved doctors → `StaffMember` `Inactive` and listed for the manager to activate or remove (they never appeared publicly) |
| 5 | Services and prices | P1 | One service per specialty, "Consultation – <Specialty>", 30 min default. One `PriceListItem` per doctor from `ConsultationFee` (doctor-specific price, valid from the migration date) |
| 6 | Schedules and slots | P2 | `DoctorSchedule.BranchId` from the doctor's branch. `AppointmentSlot.BranchId`, `StartsAtUtc`, `EndsAtUtc` computed with the branch zone; `Capacity = 1`, `BookedCount` from current status |
| 7 | Patient records | P2 | One `PatientRecord` per `PatientProfile`; MRNs assigned in `CreatedAt` order. Emergency contact and blood type move to the record. `Allergies` and `ChronicConditions` text → one `Allergy` / `Condition` entry each, flagged "migrated" for review at the next visit |
| 8 | Appointments | P2 | `BranchId` from the slot; `ServiceId` = the doctor's consultation service; `PatientRecordId` from step 7; `Source = Online`. Statuses unchanged (`Confirmed`, `Cancelled`, `Completed` are valid V2 values) |
| 9 | Reviews and notifications | P2 | `OrganizationId`; `Review.BranchId` from the appointment |
| 10 | Doctor notes → encounters | P3 | For each appointment with `DoctorNotes`: an `Encounter` with `Plan` = the notes, `Status = Signed`, `SignedAt` = appointment completion (or booking) time, author = the doctor, flagged "migrated from V1". `DoctorNotes` is then dropped in a later release |
| 11 | Drop retired columns | After P3 | `DoctorProfile.ClinicAddress`, `ClinicCity`, `ConsultationFee`, `IsApprovedByAdmin`; `PatientProfile` clinical columns; `Appointment.DoctorNotes`, `PatientProfileId` |

**Reconciliation checks** (each must match before sign-off): number of doctors = staff doctors; sum of `ConsultationFee` = sum of migrated prices; patients = patient records; appointments per status unchanged; notes count = migrated encounters; no row without `OrganizationId`.

**Rollback:** every step before 11 is reversible because nothing is dropped; the release before a migration can run against the migrated database (new columns are nullable or defaulted until the code depends on them).

## 3. API migration

| Stage | When | What |
|---|---|---|
| Freeze | Start of P0 | V1 endpoints get bug and security fixes only. `API_CONTRACT.md` §9 lists the deprecations below with dates |
| Coexist | P1–P5 | Each phase ships `/api/v2` endpoints. V1 endpoints keep working, backed by V2 services where possible |
| Deprecate | When the frontend moves a screen | The V1 endpoint returns a `Deprecation` header and a `Sunset` date |
| Remove | V2 general availability (end of P5), or earlier per endpoint by agreement | V1 controllers deleted; contract 2.x only |

Endpoints removed **earlier** because their concept disappears:

| V1 endpoint | Replacement | Removed when |
|---|---|---|
| `POST /api/auth/register/doctor` | Staff invitations (`/api/v2/staff/invitations`) | End of P1 |
| `GET /api/admin/doctors/pending`, `POST /api/admin/doctors/{id}/approve` | Staff management | End of P1 |
| `POST /api/admin/doctors/{id}/suspend` | `POST /api/v2/staff/{id}/deactivate` | End of P1 |
| `/api/Specialty` write routes (already deprecated) | `/api/admin/specialties`, then `/api/v2/...` | P0 |
| `PUT /api/appointments/{id}/notes` | Encounter endpoints | End of P3 |

## 4. Frontend migration

The frontend lives in a separate repository; each phase has a frontend track with the same exit criteria.

| Phase | Frontend work |
|---|---|
| P0 | Error handling for ProblemDetails everywhere; paged lists; timestamps with `Z`; new appointment statuses displayed |
| P1 | Staff app shell: login with MFA, `GET /api/v2/me`, permission-driven menus; admin screens (staff, roles, branches, rooms, services, prices); remove doctor sign-up and approval screens |
| P2 | Front desk (patient search/registration, calendar, booking on behalf, walk-in, check-in, queue board, waiting-room display); manager scheduling (schedules, leave, worklist); patient portal booking by branch/service with prices |
| P3 | Nurse triage screen; doctor workspace (queue, history, encounter editor, prescriptions, orders, signing); patient visit summaries |
| P4 | Checkout, invoices, payments, receipts, refunds, approvals inbox, cash sessions; online payment in the portal |
| P5 | Payers, plans, policies, eligibility, pre-auth, claims workspace |
| P6 | Reports and dashboards |

**Training and rollout:** a pilot branch first for each operational phase (P2–P5), with super-users per role, a training sandbox seeded with demo data, and a two-week parallel run (paper backup) before the next branch.

## 5. Where each open V1 issue is resolved

From [REVIEW.md](../REVIEW.md) items still open or partly fixed.

| REVIEW item | Problem | Resolved by | Phase |
|---|---|---|---|
| H4 | Editing/deleting schedules with bookings fails with 500 and half-applies | FR-SCH-03, FR-SCH-10: transactional, idempotent generation; booked appointments untouched; `Schedule.HasFutureBookings` (409) | P2 |
| H6 | Appointment paging defaults and validation | `PagedResponse<T>` helper with defaults and limits ([06 §10](06_TECHNICAL_DESIGN.md#10-data-access-and-performance)) | P0 |
| H8 | Slots run out after 14 days | `SlotGenerationJob` rolling window (FR-SCH-02) | P2 |
| M1 | Approval not enforced (unapproved doctors log in, have slots) | Doctors become invited staff; nothing to approve; `IsPublic` controls visibility | P1 |
| M2 | Local slot times compared to UTC | Branch time zones, `StartsAtUtc`, `IClock` ([ADR 0003](adr/0003-branch-time-zones.md)) | P0 |
| M3 | Expected errors return 500 | Error catalog corrected ([06 §13](06_TECHNICAL_DESIGN.md#13-errors-and-api-shapes)) | P0 |
| M5 | No global exception handler | `UseExceptionHandler` + ProblemDetails | P0 |
| M6 (partly) | No request rate limiting | ASP.NET rate limiter on auth, booking and public search endpoints | P0 |
| M12 | Docker healthcheck always fails | `/health/live`, `/health/ready` | P0 |
| M13 | Missing validators, inconsistent phone rules | Validators for every V2 request; one shared phone and password rule set | P0 (V1), each phase (V2) |
| M14 | Incomplete appointment state rules | State machine in the entity ([ADR 0004](adr/0004-appointment-state-machine.md)) | P0 |
| M15 | Doctor profile update forces a fee | Fees move to the price list; V2 `PATCH` uses nullable fields | P1 |
| L6 | Specialties can't be reactivated | `isActive` in the V2 specialty request | P1 |
| L7 (partly) | Logout needs a valid access token | V2 logout accepts the refresh token alone (it proves possession) | P1 |
| L8 (partly) | Rating recalculation in a separate save | Recalculated in the same transaction as the review | P1 |
| NM3 (partly) | Email sent inline | Outbox ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)) | P0 |
| NM5 | No fallback authorization policy | Fallback policy + public allowlist test | P0 |
| NM6 (partly) | App connects as `sa` | Least-privilege login; migrations via a deploy login ([08 §3](08_SECURITY_PRIVACY_COMPLIANCE.md#3-data-protection)) | P0 |
| NL1 (partly) | Shared read/write repository methods | Split during each module's rework | P1–P4 |
| NL2 | Unbounded lists | Paging everywhere | P0 |
| NL3 (partly) | Doctor search can't use an index | Normalised search columns ([06 §10](06_TECHNICAL_DESIGN.md#10-data-access-and-performance)) | P2 |
| NL5 (partly) | Overview stats run 6 counts | Reporting tables | P6 |
| NL6 | Reminder job round trips | Outbox batch delivery, one SMTP connection per batch | P0 |
| NL9 | Three 400 shapes, empty error bodies | ProblemDetails everywhere | P0 |
| NL10 | Controller routes behaviour by first role claim | Permissions + one service method per action | P0 |
| NL11 | Missing `date` returns `[]` | V2 slot search validates `date` (400) | P2 |
| NL12 (partly) | `EmailSettings` not validated | Provider settings validated when the channel is enabled | P0 |
| NL14 | No retry on transient SQL faults | `EnableRetryOnFailure` + execution strategy in `IUnitOfWork` | P0 |
| NL17 (partly) | `Email!` / `Name!` uses | Cleaned up with `ICurrentUser` | P0 |
| NL18 (partly) | Legacy `JwtSecurityTokenHandler` | Switch to `JsonWebTokenHandler` with V2 tokens, keeping claim names (frontend reads `roles` from `/me`, not the token) | P1 |
| NL21 | `UnitOfWork` creates repositories | Kept by decision; revisited only if decoration is needed | — |
| §4.1 | "Now" read in ~28 places | `IClock` everywhere + a test that forbids `DateTime.UtcNow` | P0 |
| §4.2 | Lists without total count | `PagedResponse<T>` | P0 |
| §4.3 | No appointment state machine | [ADR 0004](adr/0004-appointment-state-machine.md) | P0 |
| §4.4 | Notifications hand-called in services | Domain events + outbox | P0 |
| §4.5 | Fragile slot materialisation | Scheduling 2.0 | P2 |
| §4.6 | Layering leaks | `ITransaction` abstraction; drop `Services → Persistence` | P0–P1 |
| §4.7 | Role strings and hardcoded config | Role and permission constants; settings service | P0–P1 |
| §4.9 | Startup migrations and seeding | Migration bundle in the deploy step | P0 |
| §4.10 | Little test coverage, no CI | Test strategy and CI ([06 §12](06_TECHNICAL_DESIGN.md#12-testing-strategy)) | P0 |
| Duplicated logic | Three cancel flows, two review routes, double specialty CRUD, paging copies, validator copies | Replaced by the state machine, V2 routes and shared helpers | P0–P2 |
| Dead code | Unused repository methods, errors, empty folders, unused enum values | Removed or put to use (`Pending`, `NoShow` are used by V2) | P0 |
