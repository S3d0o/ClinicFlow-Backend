# 06: Technical Design

How V2 is built on top of the V1 codebase. V1 conventions in [CLAUDE.md](../../CLAUDE.md) still apply unless this document changes them explicitly.

## 1. What stays, what changes

| Area | V1 | V2 |
|---|---|---|
| Architecture | Clean Architecture, one project per layer | Same, as a modular monolith ([ADR 0006](adr/0006-clean-architecture-modular-monolith.md)). Adds an `Infrastructure` project for external providers, module folders, and architecture tests (§2) |
| Results | `Result` / `Result<T>`, errors in `Shared/Errors`; error types already corrected in V1 (M3) | Same; every V2 error code documented |
| Data access | EF Core 10, SQL Server, `IUnitOfWork`, lazy repositories | Same, plus tenancy filters, an outbox, audit interceptors, retry strategy |
| Auth | Identity + JWT + rotating refresh tokens; email login; per-IP rate limit on auth endpoints | Same tokens, plus login by phone or email, SMS one-time codes (§3.4), MFA for staff, organization and staff claims, permission policies |
| Authorization | `[Authorize(Roles = "...")]` + ownership checks in services; fallback policy and the explicit-attribute test already in place (NM5) | `[HasPermission("...")]` + scope checks in services; the fallback policy stays |
| Side effects | `TryNotifyAsync` helpers after commit | Domain events → outbox → handlers (§5) |
| Time | `IDateTimeProvider` with `ClinicNow` in one configured clinic zone (M2 fixed for one zone) | `IClock` everywhere, a time zone per branch, `StartsAtUtc` on slots (§6) |
| Background jobs | `AppointmentReminderJob` (hourly) and `SlotGenerationJob` (daily, 14 days ahead) | Several jobs with a distributed lock (§7) |
| Errors | ProblemDetails for every error, including 401/403/404/500 (M5, NL9 fixed in V1); validation keys PascalCase | Same, with camelCase validation keys (§13) |
| Payments | None | Desk payments by the receptionist, optional Paymob deposits (§9.1) |
| Languages | English emails | AR/EN templates for every email and SMS, chosen by the recipient's language |
| Real-time | None | SignalR for the queue board (§9.4) |

## 2. Solution structure

```
Domain                  entities (+ state machine methods), domain events, value objects, interfaces
                        (IUnitOfWork, ITransaction, repositories)
Shared                  DTOs, errors, settings, PagedList<T>, permission and role constants
Persistance             DbContext, configurations, migrations, repositories, UnitOfWork,
                        interceptors (tenant, audit, outbox), sequences, query implementations
                        (CQRS-lite read side: projections to response DTOs)
Services.Abstraction    service contracts, ICurrentUser, IClock, IAccessGuard, provider interfaces
                        (ISmsSender, IEmailSender, IPaymentGateway, IFileStorage, IPdfRenderer, ...)
Services                command services per module (business rules), mapping, event handlers
Infrastructure (new)    provider adapters: SmsSender (Q2 provider), EmailSender (MailKit, moved from
                        Services), PaymobGateway, FileStorage, VirusScanner, PdfRenderer, provider configs
Presentation            controllers per module, validators, filters, SignalR hubs
ClinicFlow              host: DI, middleware, JWT setup, jobs, health checks
```

- **Module folders inside each layer** keep features together: `Services/Implementations/Billing/InvoiceService.cs`, `Presentation/Controllers/Billing/InvoicesController.cs`, `Shared/DTOs/Billing/...`, `Shared/Errors/BillingErrors.cs`. New V2 modules: `Recruitment`, `Accounts`, `Billing`, `Dashboard`, and later `Chatbot`.
- `Infrastructure` references `Services.Abstraction` only. The host wires it up.
- **Drop the `Services → Persistence` project reference in P0** (REVIEW §4.6): services use `IUnitOfWork` and interfaces only, and `IDbContextTransaction` is hidden behind an `ITransaction` abstraction in Domain. MailKit and JwtBearer leave `Services` (to `Infrastructure` and the host).
- **Why drop `Services → Persistence`:** today `Services` uses no type from `Persistence`. The reference only lets a service reach `ClinicDbContext` directly, bypassing `IUnitOfWork`, which nothing in the build would catch. EF Core types also leak into services through it (`DbUpdateException` in `ReviewService`, `DbUpdateConcurrencyException` in `TokenService`, `IDbContextTransaction` on `IUnitOfWork`). Removing the reference turns "services only talk to `IUnitOfWork` and abstractions" from a convention into a compile error. It also keeps services unit-testable with substitutes, and keeps EF out of business rules: those exceptions become `Result` errors or domain exceptions mapped in `Persistence`. Neither project is removed. `Persistence` is still wired in by the host.
- **Architecture tests** (`ClinicFlow.Tests/Architecture/LayerDependencyTests.cs`, since P0 PR 2) fail the build when a layer uses a type it mustn't. They read the types each compiled layer refers to from its metadata, so they catch real use, including use the compiler allows transitively, and ignore a project reference nothing uses. Rules:
  - `Domain` uses none of our other layers. Identity's EF package is the accepted exception (ADR 0006).
  - `Services` uses neither `Persistence`, `Infrastructure` nor `Presentation`, nor EF Core, MailKit/MimeKit or the JWT libraries. Since P0 PR 3 nothing is listed as a known exception; a test fails if a listed one is no longer needed, so none can linger.
  - `Infrastructure` and `Persistence` use neither `Services` nor `Presentation` nor each other.
  - `Presentation` uses neither `Services`, `Persistence` nor `Infrastructure`. From `Domain` it may use **enums and the Result types only**: the request and response DTOs carry Domain enums (`Gender`, `BloodType`, …), and controllers turn a `Result` into a response. The Result pattern moved from `Shared` to `Domain/ResultPattern` in P0 PR 7, so entities can return it (ADR 0004). Any other Domain type fails.
  - Query classes never write (`QueryClassTests`, since P0 PR 7): the test reads the IL of everything in `Persistence.Queries`, async state machines and lambdas included, and fails on any EF Core save, change-tracking or bulk update/delete call.
- **As built (P0 PR 7):** the first CQRS-lite module is appointments. `AppointmentService` split into `AppointmentBookingService`, `AppointmentCancellationService` and `AppointmentVisitService` (commands, through `IUnitOfWork`) and `IAppointmentQueries`, implemented by `Persistance/Queries/AppointmentQueries.cs` (projections, `AsNoTracking`). `Persistence` now references `Services.Abstraction` for those interfaces; query classes are registered in DI by the host, unlike repositories. A query class does its own not-found and ownership checks and returns `Result`. The status rules moved into `Appointment` (ADR 0004).

### 2.1 Design principles

V2 follows SOLID and clean code inside this architecture. Each rule is tied to the codebase so it can be checked in review ([ADR 0006](adr/0006-clean-architecture-modular-monolith.md)).

| Principle | Rule in ClinicFlow |
|---|---|
| **Single responsibility** | One service per use-case group, not per entity: split today's large services as each module is reworked (`AppointmentService` → booking, cancellation, queries; `DoctorApplicationService` separate from staff). Controllers stay thin: the body is `return HandleResult(await service.XAsync(..., ct));` (V1 rule) |
| **Open/closed** | Status rules live in entity methods (ADR 0004, [05 §4.6](05_DOMAIN_MODEL.md#46-doctor-application)), so a new transition doesn't touch callers. A new channel (WhatsApp) or gateway is a new `IMessageChannel` / `IPaymentGateway` implementation, registered in DI; senders and services don't change |
| **Liskov substitution** | Every provider adapter honours its interface's contract: expected failures come back as `Result` errors, never exceptions; idempotent where the interface says so. One shared contract test per interface runs against every implementation, including the fakes used in tests |
| **Interface segregation** | Small, role-specific interfaces: `ICurrentUser`, `IAccessGuard`, `IClock`, `ISmsSender`, `IFileStorage`, `IPaymentGateway`, `IOtpService`; no catch-all "IntegrationService". Repositories split read and write methods where V1 shares them (NL1) |
| **Dependency inversion** | Services depend on `IUnitOfWork` and abstractions only; the compiler enforces it once `Services → Persistence` is gone, and architecture tests keep it so |
| **Clean code** | The CLAUDE.md conventions stay: Result pattern, `Feature.Reason` error codes, structured logs with named placeholders, `CancellationToken ct` last, `AsNoTracking` on reads, primary constructors. Added for V2: short, intention-named methods; no magic strings (permission, role and setting constants); one rule set for phone and password validation shared by every validator (M13); no duplicated business rules (the state machine replaces V1's three cancel flows) |
| **Mapping** | AutoMapper stays for now (decision deferred on 2026-10-23, open question Q17). New code keeps mapping in per-module profiles so it can be replaced easily; the candidate replacement is **explicit mapping** (extension methods per module), which would remove the licence question |
| **CQRS-lite** | Within a module, command services (load via `IUnitOfWork`, call entity methods, save, raise events) are separate from query services (`AsNoTracking` + `Select` projections straight to response DTOs). Query interfaces live in `Services.Abstraction` and are implemented in `Persistence`. One database, no MediatR (plain interfaces + DI), no separate read store. Used where reads and writes differ: appointments, queue, billing, recruitment, dashboard, reports ([ADR 0006](adr/0006-clean-architecture-modular-monolith.md) decision 8) |
| **Patterns on merit** | A pattern is adopted when it makes the code clearer and its cost is small, both in new modules and when **enhancing existing code**: refactoring a V1 service into commands and queries while touching it is welcome. Structural adoptions are recorded in [10 §6](10_ROADMAP.md#6-decisions-log). Not adopted: microservices, separate read databases, event sourcing |
| **Tests with every service** | When a service (command or query) is finished or changed, its public methods get unit tests in the same change (V1's `*TestsBase` + one class per method), and integration tests are added where HTTP behaviour matters: status codes, authorization and scope, validation, transactions and concurrency ([§12](#12-testing-strategy)) |

## 3. Authorization

See [ADR 0002](adr/0002-permission-based-authorization.md).

### 3.1 Tokens and claims

The access token stays small. It carries identity and membership, not permissions:

| Claim | Value |
|---|---|
| `nameidentifier` (V1) | User id |
| `org` | Organization id (staff tokens; for patients, the organization they are using) |
| `staff` | Staff member id (staff only) |
| `role` (V1, repeated) | Each role the user holds (`Doctor`, `Manager`, …) |
| `amr` | `mfa` when MFA was used at login |

Permissions are resolved on the server: `RolePermission` rows (cached per organization, invalidated on change) + `StaffRoleAssignment` rows (cached per staff member for the token lifetime). Role or assignment changes take effect at the next refresh (≤ 15 minutes), and deactivation revokes refresh tokens immediately (V1 H3 behaviour).

### 3.2 Checks

```
Controller   [HasPermission(Permissions.Appointments.CheckIn)]      → 403 if the user has it in no scope
Service      await guard.EnsureAsync(Permissions.Appointments.CheckIn, appointment.BranchId, ct)
                                                                     → Forbidden error if the branch is outside the scope
Repository   queries for lists are filtered to the user's allowed branches (guard.AllowedBranchIds(permission))
```

- `HasPermissionAttribute` uses a dynamic `IAuthorizationPolicyProvider`, so no policy needs registering per permission.
- `IAccessGuard` (Services.Abstraction) answers `Has(permission)`, `Has(permission, branchId)`, `HasOwn(permission, ownerStaffId)`, `AllowedBranchIds(permission)`. It is the only place scope logic lives, and it is unit tested against the default matrix.
- `ICurrentUser` replaces `User.GetUserId()` / `User.GetRole()` in services: user id, staff id, organization id, roles, IP.
- **As built (P0 PR 8):** the catalog, role names and default matrix are constants in `Shared/Authorization` (`Permissions`, `Roles`, `DefaultPermissionMatrix`), compared cell by cell with [02 §5–§6](02_ROLES_AND_PERMISSIONS.md#5-permission-catalog) by `PermissionMatrixTests`; `RolePermission` rows replace the matrix when `roles.manage` arrives (P1). Permissions come from the token's role claims through the matrix, with no cache yet since nothing is stored. `IAccessGuard` has two members: `ScopeOf(permission)` (the widest scope, or null) and `CanAccessAsync(permission, AccessTarget(ownerUserId, branchId), ct)`, which returns `bool` so each service keeps its own contract error code (e.g. `Appointment.Unauthorized`). Until P1 assigns staff to branches, `Branch` covers every record like `Organization` (one branch, no assignments); `AllowedBranchIds` comes with the assignments. `ICurrentUser` (`Presentation/Authorization/HttpCurrentUser`) has the user id, roles and IP; staff and organization ids come with P1's staff members (the organization is `ITenantContext`'s). The provider, handler and `[HasPermission]` live in `Presentation/Authorization`; no inherited route uses them yet, so an integration test checks the registration directly. `AuthorizationTests.AnonymousEndpoints_AreExactlyTheAllowlist` lists every public endpoint. Matrix cells that depend on the staff member rather than the role ("org managers only", "medical director") wait for P1's staff flags.
- **Fallback policy** (already in V1): authenticated user required. Public endpoints carry `[AllowAnonymous]` explicitly (doctor/service search, slots, specialties, auth and code endpoints, payment webhooks with signature verification). V1's `AuthorizationTests.EveryControllerAction_DeclaresAuthorizeOrAllowAnonymous` is kept and extended with an allowlist of anonymous endpoints.
- **Doctor applicants** have an account but no role and no staff membership. Their endpoints (`/api/v2/doctor-applications/mine`) only need an authenticated user and check ownership in the service.
- **Hard rules in code:** clinical permissions are never granted to `Admin` or `Accountant`, whatever `RolePermission` says.
- **Break-glass:** `POST /api/v2/patients/{id}/break-glass` with a reason grants a temporary `BreakGlassGrant(staffId, patientRecordId, expiresAt)` row; the guard checks it for clinical reads; every use writes an audit entry flagged `BreakGlass`.

### 3.3 MFA

ASP.NET Identity's authenticator (TOTP) support. Login for staff becomes two steps: password → `mfaRequired` with a short-lived MFA token → `POST /api/v2/auth/mfa` with the code → tokens. Recovery codes are generated at enrolment. Admin can reset a staff member's MFA (audited).

### 3.4 Sign-in by phone and SMS codes

- **Identity:** phone numbers are normalised to E.164 (`+201…`) by one shared helper and stored in Identity's `PhoneNumber` with a unique index; `PhoneNumberConfirmed` records verification. For accounts without an email, `UserName` is the E.164 phone, and `RequireUniqueEmail` is relaxed so `Email` can be null (a unique filtered index keeps non-null emails unique).
- **Login:** `POST /api/v2/auth/login { identifier, password }`. The service decides by shape (contains `@` → email, else phone) and finds the user by normalised email or phone. One generic error for "no such account" and "wrong password" (V1 behaviour).
- **Codes:** an `IOtpService` (Services) issues and checks `PhoneVerificationCode` rows: 6 random digits from a cryptographic RNG, stored as an HMAC-SHA256 hash, 5 minutes, 5 attempts, 60 s resend cooldown, daily caps per number and per IP, compared in constant time. It calls `ISmsSender` **directly**, not through the outbox, because the code expires in minutes; a send failure returns an error so the user can retry.
- **Rate limiting:** a new `RateLimitPolicies.Otp` policy on the send and verify endpoints, in addition to the per-number caps (V1's `RateLimitPolicies.Auth` pattern).
- **Purposes:** `SignUp`, `ChangePhone`, `PasswordReset`; a code is valid only for its purpose and number.

## 4. Tenancy

See [ADR 0001](adr/0001-single-organization-tenant-ready.md).

- `IOrganizationOwned { int OrganizationId }` on every organization-owned entity.
- `ITenantContext.OrganizationId` is resolved per request: from the `org` claim for staff; for patients and anonymous public endpoints, from the request host or, in V2, the single configured organization.
- **EF global query filter** on every `IOrganizationOwned` type: `e => e.OrganizationId == tenant.OrganizationId`. A unit test reflects over the model and fails if an `IOrganizationOwned` entity has no filter.
- **SaveChanges interceptor** sets `OrganizationId` on new entities and rejects any write whose `OrganizationId` differs from the context (defence in depth).
- Background jobs run per organization: they iterate organizations and set the tenant context explicitly (one iteration in V2).
- Unique indexes include `OrganizationId` (e.g. `(OrganizationId, Mrn)`, `(OrganizationId, Code)`).
- **As built (P0 PR 5):** `IOrganizationOwned` / `IBranchOwned` live in `Domain/Entities/Tenancy`, `ITenantContext` in `Domain/Interfaces`. In V2 it is `SingleOrganizationTenantContext` (organization 1, branch `MAIN` = 1), both rows inserted by the `AddOrganizationAndBranches` migration (`HasData`), which also backfills existing rows. `ClinicDbContext` takes the tenant in its constructor and adds the filter and the foreign keys to every type that implements the interfaces; the interceptor reads the organization from the context it saves, so a second context for another organization works in tests. Organization-owned so far: `Branch`, `DoctorProfile`, `DoctorSchedule`, `AppointmentSlot`, `Appointment`, `Review`, `Notification`; branch-owned: schedules, slots, appointments. `DoctorProfile.UserId` stays one-to-one with the user (a doctor in several organizations is a V3 question for `StaffMember`).

## 5. Domain events and the outbox

V1 calls notifications and emails inline after the commit and swallows failures (M4, REVIEW §4.4). V2 replaces this with a transactional outbox.

```
Service: appointment.Cancel(...)  →  entity records AppointmentCancelled event
UnitOfWork.SaveChangesAsync()     →  interceptor serializes events into OutboxMessage rows (same transaction)
OutboxDispatcher (BackgroundService, every 2 s)
    claims a batch:  UPDATE TOP (50) ... WITH (UPDLOCK, READPAST) SET LockedUntil = ...
    for each message → handlers (in-app notification, email, SMS/WhatsApp, read-model update, webhook)
    success → ProcessedAt; failure → Attempts++, exponential backoff, dead-letter after 10 attempts
```

- Delivery is **at least once**. Handlers are idempotent (they check `MessageDelivery` for `(OutboxMessageId, Channel)` before sending).
- The request never waits for SMTP or SMS (fixes NM3). The outbox lag is a health metric.
- Event examples: `AppointmentBooked`, `AppointmentRescheduled`, `AppointmentCancelled`, `PatientCheckedIn`, `DepositPaid`, `EncounterSigned`, `InvoiceIssued`, `PaymentReceived`, `RefundApproved`, `LeaveApproved`, `StaffInvited`, `DoctorApplicationStageChanged`, `ClaimSubmitted`.
- **Channel choice:** a handler for a patient-facing event reads the recipient's `NotificationChannel` (`Sms`, `Email`, `Both`) and `PreferredLanguage`, renders the matching `MessageTemplate`, and sends through each chosen channel, one `MessageDelivery` per channel.
- **Stage emails** for doctor applications are rendered from the variables stored on the stage change, so a retry sends exactly what the reviewer previewed.
- **Not through the outbox:** SMS verification codes (§3.4), which expire in minutes.
- **As built (P0 PR 9):** `OutboxMessage` (type name, JSON payload, attempts, `NextAttemptAtUtc`, `LockedUntilUtc`, processed and dead-letter times, last error) and `MessageDelivery` (unique per message and channel), both organization-owned. Entities implement `IHasDomainEvents`; `OutboxSaveChangesInterceptor` takes their events in `SavingChanges` and adds the rows, before the tenant interceptor stamps them. Services add events no entity raises with `uow.Outbox.Add` (registration and reset emails, doctor approval and suspension, and `AppointmentBooked`, which needs the inserted id and is added after the insert in the same transaction). `OutboxProcessor` (Persistence) claims up to 50 due rows with `UPDLOCK, READPAST` and a 5-minute claim, runs each message's handlers in their own scope, then marks it processed, or schedules a retry (2 s, 4 s, … capped at an hour) or dead-letters it after 10 attempts. A new message has no `NextAttemptAtUtc` and is due at once, so the app's clock alone decides backoff. `OutboxDispatcher` (host) calls it every 2 s under `sp_getapplock`, so one instance dispatches. Handlers live in `Services/EventHandlers`, one per event, and send each channel through `Delivery.OnceAsync`: in-app notifications are saved with their delivery record (exactly once), emails are recorded after sending (at least once). Email tokens are created by the handler when it sends, never stored. The reminder job only marks reminders and queues `AppointmentReminderDue`. The demo seeder switches recording off (`RecordDomainEvents`). Not built: SMS and channel preferences (later phases), the outbox-lag health check (PR 11), and per-message tenant switching for V3 (the processor reads every organization's rows; handlers run with the app's tenant).

### 5.1 Audit trail

FR-AUD-01 and FR-AUD-04: every change to a clinical, financial or access-control record writes an `AuditEntry` in the same transaction, and entries are never changed or deleted.

- **As built (P0 PR 10):**
  - **What is audited.** A type opts in from its EF configuration: `builder.IsAudited("Name")`. Properties are recorded with old and new values unless marked `IsAuditRedacted()` (only `{"changed": true}`: clinical values are never copied, [08 §4](08_SECURITY_PRIVACY_COMPLIANCE.md#4-access-control-and-audit)) or `IsNotAudited()`. `IsAudited("Name", AuditMode.Ignored)` records only the properties marked `IsAudited()`. The marks are model annotations, so they live next to the entity's other rules, and `AuditModelTests` lists the audited types and modes.
  - **Audited in P0:** `Appointment` (every booking and transition; `ReasonForVisit` and `DoctorNotes` redacted, the reminder mark ignored), `DoctorProfile` (approval and the profile; the rating cache ignored), `User` (only `IsActive`, i.e. suspension: the rest is Identity's bookkeeping) and `UserRole` (role changes, with the role's name).
  - **The entry.** `AuditEntry` (organization-owned): `OccurredAtUtc` (database default), `UserId` and `IpAddress` from `ICurrentUser` (null when nobody is signed in: registration, jobs), `Action` (`Create`, `Update`, `Delete`; stored as a string, so later actions such as `Read` or `Export` need no data migration), `EntityType`, `EntityId` (a composite key's values joined by `,`), `Changes` (JSON), `Reason` (no P0 action requires one), and `CorrelationId` (the request's trace id, shared by every entry of the request). No foreign key to the user, so an entry outlives the account. 05's `StaffMemberId` and `PatientRecordId` come with the entities they point at (P1, P2).
  - **How.** `AuditSaveChangesInterceptor` runs after the outbox interceptor and before the tenant one, which stamps the entries. An update that touches only ignored properties writes nothing. A row whose key is an identity column has no id until it is inserted, so its entry is added by a second save straight after the first. That is only atomic inside a transaction, so such an insert outside `ExecuteInTransactionAsync` throws (bookings and registrations already run in one). `ExecuteUpdate`/`ExecuteDelete` skip interceptors, so they must not touch audited properties.
  - **Append-only.** No repository or endpoint writes entries, and the interceptor throws on a modified or deleted `AuditEntry`, even with auditing off. The database denial of `UPDATE`/`DELETE` to the app login comes with PR 11, once that login stops being `db_owner`.
  - **Switch.** `ClinicDbContext.RecordAudit`: the demo seeder and the integration tests' `SeedAsync` turn it off, since stored history isn't an action anyone took.

## 6. Time and time zones

See [ADR 0003](adr/0003-branch-time-zones.md).

- `IClock` replaced V1's `IDateTimeProvider` in P0 PR 4 (same `UtcNow` and `ClinicNow` for the one configured `Clinic:TimeZoneId`, plus `ClinicToday`; `Clock` reads `TimeProvider.System`). PR 6 adds `Today(branchId)`, `LocalNow(branchId)`, `ToUtc(branchId, date, time)`. `ClinicNow` becomes "now in the default branch" and is phased out as code moves to branch-aware calls. Every "now" in V2 code goes through it (REVIEW §4.1). `ClockUsageTests` scans the source of every production project for `DateTime.UtcNow` / `Now` / `Today`, `DateTimeOffset.Now` / `UtcNow` and `TimeProvider.System` (allowed only where `Clock` is registered) and fails. Every `DateTime` column holds UTC and is read back as UTC (`UtcDateTimeConverter`), so API timestamps end in `Z`.
- Branch zones are IANA ids (`Africa/Cairo`), resolved with `TimeZoneInfo.FindSystemTimeZoneById`, which accepts IANA ids on Linux and on Windows with .NET 6+ ICU.
- Slots keep `Date` + `StartTime` (local, what the patient sees) and store `StartsAtUtc` / `EndsAtUtc`, computed at generation. All comparisons ("in the past?", "2 hours before?", reminders, no-show grace) use the UTC columns. Fixes M2.
- **Daylight saving** (Egypt has observed it again since 2023): invalid local times (the skipped hour) are not generated; ambiguous times (the repeated hour) use the first occurrence. If the zone's rules change after slots exist, a job recomputes `StartsAtUtc` for future slots.
- API timestamps are serialized with `Z` (V1 quirk fixed; see [07 §3](07_API_DESIGN.md#3-conventions)).
- **As built (P0 PR 6):** `Domain.Time.WallClock` holds the conversion rule (skipped time → no instant, repeated time → first occurrence); `Clock`, the demo seeder and the tests share it. `IClock` gained `LocalNow`, `Today` and `ToUtc`, taking the branch's zone id from `uow.Branches` (ADR 0003 amendment). Slot generation and schedule maintenance use the schedule's branch zone; the four appointment rules and the reminder query compare `StartsAtUtc` with `UtcNow`. The `AddSlotUtcTimes` migration fills existing slots with `AT TIME ZONE`, mapping each branch's IANA zone to SQL Server's Windows name from .NET's zone data, and refuses to finish if a zone has no mapping; `AddSlotUtcTimesTests` checks it against .NET around both 2026 Cairo changes.

## 7. Background jobs

| Job | Schedule | Purpose | Fixes / FR |
|---|---|---|---|
| `OutboxDispatcher` | Continuous (2 s poll) | Deliver events | §5 |
| `SlotGenerationJob` | Daily 02:00 branch time, and on schedule change | Exists in V1 (daily, 14 days ahead, one clinic zone; fixed H8). V2 makes it per branch with a 28-day window (setting); idempotent (`MERGE` on the unique key) | FR-SCH-02 |
| `ReminderJob` | Every 5 minutes | Replaces V1's hourly `AppointmentReminderJob`: reminders at configured offsets, using `StartsAtUtc`, by the patient's chosen channels | FR-APT-09, FR-NTF-04 |
| `StaffActivationJob` | Daily 00:05 branch time | Activate staff members from accepted applications whose start date has come | FR-REC-05 |
| `ApplicationFileRetentionJob` | Daily | Delete CV and cover-letter files of rejected or withdrawn applications past the retention period | FR-REC-08 |
| `VerificationCodeCleanupJob` | Hourly | Delete phone verification codes older than a day | FR-ACC-02 |
| `DayEndJob` | Daily 23:30 branch time | List unresolved past appointments; flag unsigned encounters; remind about open cash sessions | FR-APT-07, FR-EMR-09 |
| `ClaimDeadlineJob` | Daily | Alert on invoice lines approaching the payer's submission deadline | FR-INS-07 |
| `InvitationCleanupJob` | Daily | Expire invitations and break-glass grants | FR-STF-01 |
| `ReportingRefreshJob` | Every 15 minutes | Refresh reporting tables (§10) | FR-RPT-* |

- Jobs remain `BackgroundService` classes (V1 pattern), each creating a scope per run.
- **Only one instance runs each job:** a SQL Server application lock (`sp_getapplock`) per job name, taken at the start of each run (fixes the M9 multi-instance note). No new infrastructure is needed.
- If job needs grow (retries, dashboards, cron expressions), switch to Hangfire on the same SQL Server; recorded as an open decision ([10 §5](10_ROADMAP.md#5-open-questions)).
- Integration tests remove all jobs, as V1's `ClinicFlowApiFactory` removes `AppointmentReminderJob` and `SlotGenerationJob`.

## 8. Files and documents

- `IFileStorage`: `PutAsync`, `OpenReadAsync`, `DeleteAsync`. Implementations: local disk (Development), Azure Blob Storage or an S3-compatible store (Production; decision in [10 §5](10_ROADMAP.md#5-open-questions)). Files are never stored in SQL Server. **Needed from P1** for doctor CVs and cover letters (it was planned for the EMR phase); every file is a `StoredFile` row ([05 §3.1a](05_DOMAIN_MODEL.md#31a-recruitment)).
- Uploads go through the API: size and type checks per use (CVs: PDF, DOCX, ≤ 5 MB; clinical: PDF, JPEG, PNG, HEIC, ≤ 20 MB; DICOM excluded in V2), SHA-256 hash, **virus scan** (ClamAV container) before the file becomes visible (`ScanStatus`).
- Downloads go through an API endpoint that checks permissions, writes a read audit entry, and streams the file. No public URLs.
- `IPdfRenderer` for invoices, receipts, prescriptions, referral forms and statements. It needs Arabic (RTL) text shaping; QuestPDF is the leading candidate (check its licence against the clinic's revenue), with a fallback of HTML → PDF via headless Chromium.
- Every generated document carries a **verification code** (random, stored with the document) so a pharmacy or insurer can check authenticity at a public verification page that shows only minimal data.

## 9. Integrations

### 9.1 Payments

Two ways to pay, both first-class:

| Way | Who | How |
|---|---|---|
| **At the desk** (the usual way in Egypt) | Receptionist | Cash or card (standalone POS terminal) at check-in or checkout, recorded with `payments.collect` in the receptionist's open cash session; the POS approval code goes in `Reference`. No gateway involved |
| **Online deposit** (optional) | Patient | Paymob, when booking or later from the appointment. Never required; the booking is already confirmed |

- **Gateway: Paymob** (decided 2026-10-22, Q1). `IPaymentGateway` (Services.Abstraction) with `PaymobGateway` (Infrastructure): `CreatePaymentAsync(amount, reference, customer, returnUrl)` → Paymob payment intention → hosted checkout URL; `VerifyWebhook(request)` → checks Paymob's HMAC with the account's HMAC secret; `RefundAsync(transactionId, amount)`.
- Card data never touches ClinicFlow: Paymob's hosted checkout (cards and mobile wallets), so the PCI DSS scope stays at SAQ A.
- **Webhooks are the source of truth:** `POST /api/v2/payments/webhooks/paymob` (anonymous + HMAC check), idempotent on Paymob's transaction id; the amount and the merchant order reference must match the stored intention. The return URL only shows the result.
- Configuration: API key, integration ids (card, wallet), iframe id, HMAC secret: secrets, never in `appsettings.json`. A Paymob sandbox is used in Development and Staging.
- Another gateway later (Fawry, Kashier) is a new `IPaymentGateway` implementation selected by configuration (§2.1, open/closed).

### 9.2 SMS and WhatsApp

- **SMS is needed from P1** (sign-up codes), not P2. The provider is open question Q2 (a local Egyptian aggregator or an international one); `ISmsSender` keeps the choice behind one interface, with a fake sender in Development and tests that logs the message.
- `IMessageChannel` per channel (`Sms`, `Email`, later `WhatsApp`), chosen per message from the recipient's preference (§5). WhatsApp Business requires pre-approved templates, so `MessageTemplate` keeps the provider template id when it is added.
- SMS bodies are short and in the recipient's language; Arabic SMS use UCS-2, so a segment is 70 characters. Templates are checked for length.
- Phone numbers are normalised to E.164 everywhere by one shared helper (§3.4).
- A **spend alert** fires when daily SMS volume passes a threshold (setting), to catch abuse ([08 §2](08_SECURITY_PRIVACY_COMPLIANCE.md#2-threats-and-controls)).

### 9.3 Email

MailKit stays, moved into `Infrastructure` and called only from outbox handlers, with one SMTP connection per batch (NL6). V1's English-only email bodies become AR/EN `MessageTemplate` rows (Arabic emails are `dir="rtl"`).

### 9.4 Real-time queue

- A SignalR hub `/hubs/queue`. Clients join group `branch:{id}` (checked against `queue.view`). Queue changes publish an in-process event that pushes a compact update.
- The waiting-room display authenticates with a long-lived **device token** limited to the display's branch, showing ticket numbers and rooms only.
- With more than one API instance, SignalR needs a backplane (Redis or Azure SignalR). V2 starts single-instance; the backplane is added when scaling out.

### 9.5 Chatbot (P7)

- Built last; the design is confirmed at the P7 kickoff (Q16). Working assumption: a `ChatbotService` behind `IChatAssistant` calls a hosted LLM (for example the Claude API) with **tool calls into existing V2 services** (public branches, services, prices, slot search, booking), so the bot never bypasses validation, permissions or the state machine.
- Grounded only in clinic data and an approved FAQ; no medical advice; no clinical data sent to the provider without legal approval ([08 §5](08_SECURITY_PRIVACY_COMPLIANCE.md#5-privacy-and-consent)); per-user rate limits and a cost cap.

## 10. Data access and performance

- **Reads:** `AsNoTracking()` and a `Select` projection to the response type for every list (NL1, NM4; replaces AutoMapper's `ProjectTo<T>()` if explicit mapping is adopted, §2.1); split read and write repository methods where V1 shares them.
- **Paging:** V1's shape stays: the service returns `Result<PagedList<T>>`, the controller uses `HandlePagedResult`, the body is a JSON array and the total goes in the `X-Total-Count` header (H6 and NL2 fixed in V1). V1's limits differ per endpoint (10–50 by default, max 20–100); V2 lists use one default (1/20) and one max page size (100) through a shared `PageRequest` validator rule. Every V2 list sends `X-Total-Count`.
- **Dashboard (FR-RPT-11):** one query per chart over indexed columns (`GROUP BY` day and status, as V1's appointment stats do), cached for 60 s per branch filter; moved onto reporting tables in P6 if it gets slow.
- **Patient search:** a persisted computed column `SearchName` (normalised Arabic and Latin), indexed; phone search on a normalised digits column. Full-text search is considered if search gets slow at 200k patients.
- **Reports:** reporting tables (e.g. `DailyBranchStats`, `DoctorUtilisation`) refreshed by `ReportingRefreshJob` from the transactional tables; dashboards never run heavy aggregates on hot tables. Live numbers (queue length, today's bookings) are direct indexed queries.
- **Indexes** planned with each migration: every foreign key; `(BranchId, Date)` on slots and appointments; `(PatientRecordId, CreatedAt)` on encounters; `(Status, BranchId)` on invoices; `(OrganizationId, Mrn)` unique.
- **Retry on transient faults** (NL14, since P0 PR 3): `EnableRetryOnFailure`. Manual transactions run through `IUnitOfWork.ExecuteInTransactionAsync`, which wraps them in the execution strategy (`Persistance/Transactions`). A retry re-runs the operation on the same DbContext, so the change tracker is rewound to its state before the first attempt: entities added during the attempt are detached with their identity keys reset, and tracked entities get their values and states back. Without that, a retried booking would insert twice, and a change saved in the failed attempt would count as saved and be lost. An exception after the commit is never retried. Side effects stay outside the operation until the outbox (PR 9) takes them over.

## 11. Concurrency and idempotency

| Risk | Mechanism |
|---|---|
| Two bookings for one slot | Conditional `UPDATE ... WHERE BookedCount < Capacity` in the booking transaction (the V1 pattern, generalised) |
| Two staff editing the same encounter, appointment or invoice | `RowVersion` → `409 Concurrency.Conflict`; the client reloads |
| Double-click / retry on payment or booking | `Idempotency-Key` header on `POST` payments, refunds and bookings; `IdempotencyRecord(key, userId, requestHash, responseJson, expiresAt)`; the same key returns the stored response |
| Invoice number gaps or duplicates | `UPDATE InvoiceNumberSequence SET LastNumber = LastNumber + 1 OUTPUT inserted.LastNumber WHERE BranchId = @b AND Year = @y` inside the issuing transaction |
| Gateway webhook delivered twice | Unique index on `(Gateway, GatewayTransactionId)` on `Payment` |
| Two reviewers moving the same application | `RowVersion` on `DoctorApplication` → `409 Concurrency.Conflict` |
| Job running twice | `sp_getapplock` (§7) |

## 12. Testing strategy

| Level | What | Tooling |
|---|---|---|
| Unit | State machines (appointment and doctor application: every legal and illegal transition), price resolution, coverage split, invoice totals, deposit deduction, discount limits, slot generation including DST, OTP rules (expiry, attempts, cooldown), phone normalisation, template rendering in both languages, `IAccessGuard` against the default matrix | xUnit + NSubstitute (V1 conventions) |
| Architecture | Forbidden project references (§2), controllers inherit `ApiController`, services don't reference `ClinicDbContext` | Metadata-reading tests in `ClinicFlow.Tests/Architecture` (no package needed) |
| Provider contracts | Each `IPaymentGateway`, `ISmsSender`, `IFileStorage` implementation and its fake pass the same contract tests (§2.1) | xUnit; Paymob sandbox in a manual/nightly run |
| Integration | Every endpoint: success, validation, not-found, and **permission matrix tests** generated from [02 §6](02_ROLES_AND_PERMISSIONS.md#6-role--permission-matrix) (each role × permission × in-scope / out-of-scope branch) | WebApplicationFactory + Testcontainers SQL Server (V1); `TestAuthHandler` gains `X-Test-Org`, `X-Test-Staff`, `X-Test-Branches` headers |
| Tenancy | A second organization is seeded in every integration test class; tests assert its data never appears | Integration |
| Outbox | Events written with the business change; handler idempotency; retry and dead-letter | Integration |
| Contract | `openapi.json` regenerated and diffed in CI; breaking changes fail unless the contract version is bumped | CI script |
| Load | Booking burst on one slot (exactly one wins), queue board with 50 clients, search at 200k patients | k6 |
| Import | The V1 → V2 import tool: one test per step against a V1 sample database (V1's demo seed), run in CI so a V2 schema change that breaks the import fails the build; a full dry run on a copy of real data before any switch, with row-count and checksum reports | Integration + tool ([09 §2](09_MIGRATION_FROM_V1.md#2-importing-v1-data)) |

CI (GitHub Actions; V1 has none, REVIEW §4.10): build with warnings as errors, unit tests, integration tests (Docker service), openapi diff, `dotnet list package --vulnerable`.

## 13. Errors and API shapes

- **Already in V1:** `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`, so every error, including 401, 403, 404 and unhandled 500s, returns ProblemDetails (M5, NL9); the async `ValidationFilter` (NL8); the corrected error catalog, where `Failure` means an unexpected server error only (M3).
- **V2 changes:** validation keys become camelCase property paths in `/api/v2` responses (V1 stays PascalCase until it is removed).
- Error codes are listed per module in the V2 API contract, generated from `Shared/Errors` to avoid drift.

## 14. Operations

- **Migrations move out of startup** into a deploy step (`dotnet ef migrations bundle`), so several instances can start safely (REVIEW §4.9). V1 still runs `Database.MigrateAsync()` in `DataSeeder.SeedAsync` on every start; V2 startup only checks that the database is at the expected migration, and demo seeding stays behind `Seeding:DemoData`.
- **Health checks:** V1 maps `/health` (anonymous). V2 splits it into `/health/live` and `/health/ready` (database, outbox lag, storage, SMS and Paymob reachability) (M12).
- **Metrics:** queue length per branch, median wait, outbox lag, job duration and failures, payment webhook failures, login failures.
- **Logs:** Serilog as V1, with a destructuring policy that masks phone numbers, national ids and clinical text; no request or response bodies on clinical or payment endpoints.
- **Separate from V1** ([ADR 0007](adr/0007-v2-separate-product-line.md)): V2 has its own database (`ClinicFlowV2`), connection string, user secrets id, compose project name, container names and host ports, so a V1 and a V2 stack can run side by side on one machine without touching each other. V2 never runs migrations against V1's database; the import tool reads V1 with a read-only login.
- **Environments:** Development (V2 demo seed), Staging (V2 demo data, plus a test import of an anonymised V1 copy), Production (only once the owner chooses V2).
- **Backups:** per [04 NFR-04](04_FUNCTIONAL_REQUIREMENTS.md#5-non-functional-requirements); file storage is backed up with the same retention.
- **As built (P0 PR 11):**
  - **Migrations.** `Database:MigrateOnStartup` (default `true` in Development only) decides whether startup migrates. When it is off, `DataSeeder.EnsureSchemaAsync` checks the pending migrations and refuses to start, naming them. The Docker image carries an EF migration bundle (`/app/efbundle`). Compose runs it in a one-shot `migrate` service (`db` → `db-init` → `migrate` → `backend`) as a deploy login, `clinicflow_v2_deploy`, which is `db_owner` of `ClinicFlowV2` only.
  - **Least privilege.** The app's login, `clinicflow_v2_app`, is a member of the `clinicflow_app` database role. The `AddAppDatabaseRole` migration gives the role `db_datareader` and `db_datawriter` and denies it `UPDATE` and `DELETE` on `AuditEntries`, so the rule exists wherever the schema does and is tested against the real server (`AppDatabaseRoleTests`). `db-init.sql` creates both logins, adds the app's login to the role, and takes it out of `db_owner` on an older volume.
  - **Probes.** `/health/live` (and `/health`, V1's route, now an alias) runs no checks: an orchestrator restarts the process only when it hangs, never because the database is down. `/health/ready` runs the `database` check (`CanConnectAsync`; Unhealthy → 503) and the `outbox` check. The outbox check is Degraded (still 200) when the oldest due message has waited more than a minute; messages backing off or dead-lettered don't count. The body is JSON with each check's status and data, and never exception text, since the route is anonymous. The Dockerfile `HEALTHCHECK` probes `/health/live`.
  - **Not built:** storage, SMS and Paymob reachability come with those integrations, and metrics are not built yet.
