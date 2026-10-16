# 06: Technical Design

How V2 is built on top of the V1 codebase. V1 conventions in [CLAUDE.md](../../CLAUDE.md) still apply unless this document changes them explicitly.

## 1. What stays, what changes

| Area | V1 | V2 |
|---|---|---|
| Architecture | Clean Architecture, one project per layer | Same. Adds an `Infrastructure` project for external providers (§2) |
| Results | `Result` / `Result<T>`, errors in `Shared/Errors` | Same. Error types corrected (M3); every error code documented |
| Data access | EF Core 10, SQL Server, `IUnitOfWork`, lazy repositories | Same, plus tenancy filters, an outbox, audit interceptors, retry strategy |
| Auth | Identity + JWT + rotating refresh tokens | Same tokens, plus MFA for staff, organization and staff claims, permission policies |
| Authorization | `[Authorize(Roles = "...")]` + ownership checks in services | `[HasPermission("...")]` + scope checks in services; fallback policy (NM5) |
| Side effects | `TryNotifyAsync` helpers after commit | Domain events → outbox → handlers (§5) |
| Time | `IDateTimeProvider` in some places, UTC vs local bug (M2) | `IClock` everywhere, branch time zones (§6) |
| Background jobs | One hourly `BackgroundService` | Several jobs with a distributed lock (§7) |
| Errors | Three 400 shapes, empty 401/403/404/500 (M5, NL9) | ProblemDetails for every error (§13) |
| Real-time | None | SignalR for the queue board (§9.4) |

## 2. Solution structure

```
Domain                  entities (+ state machine methods), domain events, value objects, interfaces
Shared                  DTOs, errors, settings, PagedResponse<T>, permission and role constants
Persistance             DbContext, configurations, migrations, repositories, UnitOfWork,
                        interceptors (tenant, audit, outbox), sequences
Services.Abstraction    service contracts, ICurrentUser, IClock, IAccessGuard, provider interfaces
Services                business logic per module, AutoMapper profiles, event handlers
Infrastructure (new)    IPaymentGateway, IMessageSender (SMS/WhatsApp), IFileStorage, IPdfRenderer,
                        IVirusScanner, email (moved from Services), provider configs
Presentation            controllers per module, validators, filters, SignalR hubs
ClinicFlow              host: DI, middleware, jobs, health checks
```

- **Module folders inside each layer** keep features together: `Services/Implementations/Billing/InvoiceService.cs`, `Presentation/Controllers/Billing/InvoicesController.cs`, `Shared/DTOs/Billing/...`, `Shared/Errors/BillingErrors.cs`.
- `Infrastructure` references `Services.Abstraction` only. The host wires it up.
- Drop the `Services → Persistence` project reference over time (REVIEW §4.6): services use `IUnitOfWork` and interfaces only, and `IDbContextTransaction` is hidden behind an `ITransaction` abstraction in Domain.

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
- **Fallback policy:** authenticated user required. Public endpoints carry `[AllowAnonymous]` explicitly (doctor/service search, slots, specialties, auth endpoints, payment webhooks with signature verification).
- **Hard rules in code:** clinical permissions are never granted to `Admin` or `Accountant`, whatever `RolePermission` says.
- **Break-glass:** `POST /api/v2/patients/{id}/break-glass` with a reason grants a temporary `BreakGlassGrant(staffId, patientRecordId, expiresAt)` row; the guard checks it for clinical reads; every use writes an audit entry flagged `BreakGlass`.

### 3.3 MFA

ASP.NET Identity's authenticator (TOTP) support. Login for staff becomes two steps: password → `mfaRequired` with a short-lived MFA token → `POST /api/v2/auth/mfa` with the code → tokens. Recovery codes are generated at enrolment. Admin can reset a staff member's MFA (audited).

## 4. Tenancy

See [ADR 0001](adr/0001-single-organization-tenant-ready.md).

- `IOrganizationOwned { int OrganizationId }` on every organization-owned entity.
- `ITenantContext.OrganizationId` is resolved per request: from the `org` claim for staff; for patients and anonymous public endpoints, from the request host or, in V2, the single configured organization.
- **EF global query filter** on every `IOrganizationOwned` type: `e => e.OrganizationId == tenant.OrganizationId`. A unit test reflects over the model and fails if an `IOrganizationOwned` entity has no filter.
- **SaveChanges interceptor** sets `OrganizationId` on new entities and rejects any write whose `OrganizationId` differs from the context (defence in depth).
- Background jobs run per organization: they iterate organizations and set the tenant context explicitly (one iteration in V2).
- Unique indexes include `OrganizationId` (e.g. `(OrganizationId, Mrn)`, `(OrganizationId, Code)`).

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
- Event examples: `AppointmentBooked`, `AppointmentConfirmed`, `AppointmentRescheduled`, `AppointmentCancelled`, `PatientCheckedIn`, `EncounterSigned`, `InvoiceIssued`, `PaymentReceived`, `RefundApproved`, `LeaveApproved`, `StaffInvited`, `ClaimSubmitted`.

## 6. Time and time zones

See [ADR 0003](adr/0003-branch-time-zones.md).

- `IClock` (extends V1 `IDateTimeProvider`): `UtcNow`, `Today(branchId)`, `LocalNow(branchId)`, `ToUtc(branchId, date, time)`. Every "now" in V2 code goes through it (REVIEW §4.1). A test greps the solution for `DateTime.UtcNow` / `DateTime.Now` outside `Clock` and fails.
- Branch zones are IANA ids (`Africa/Cairo`), resolved with `TimeZoneInfo.FindSystemTimeZoneById`, which accepts IANA ids on Linux and on Windows with .NET 6+ ICU.
- Slots keep `Date` + `StartTime` (local, what the patient sees) and store `StartsAtUtc` / `EndsAtUtc`, computed at generation. All comparisons ("in the past?", "2 hours before?", reminders, no-show grace) use the UTC columns. Fixes M2.
- **Daylight saving** (Egypt has observed it again since 2023): invalid local times (the skipped hour) are not generated; ambiguous times (the repeated hour) use the first occurrence. If the zone's rules change after slots exist, a job recomputes `StartsAtUtc` for future slots.
- API timestamps are serialized with `Z` (V1 quirk fixed; see [07 §3](07_API_DESIGN.md#3-conventions)).

## 7. Background jobs

| Job | Schedule | Purpose | Fixes / FR |
|---|---|---|---|
| `OutboxDispatcher` | Continuous (2 s poll) | Deliver events | §5 |
| `SlotGenerationJob` | Daily 02:00 branch time, and on schedule change | Extend slots to the rolling window; idempotent (`MERGE` on the unique key) | H8, FR-SCH-02 |
| `ReminderJob` | Every 5 minutes | Reminders at configured offsets, using `StartsAtUtc` | M2, FR-APT-09 |
| `DepositExpiryJob` | Every minute | Cancel `Pending` appointments past their hold time | FR-APT-08 |
| `DayEndJob` | Daily 23:30 branch time | List unresolved past appointments; flag unsigned encounters; remind about open cash sessions | FR-APT-07, FR-EMR-09 |
| `ClaimDeadlineJob` | Daily | Alert on invoice lines approaching the payer's submission deadline | FR-INS-07 |
| `InvitationCleanupJob` | Daily | Expire invitations and break-glass grants | FR-STF-01 |
| `ReportingRefreshJob` | Every 15 minutes | Refresh reporting tables (§10) | FR-RPT-* |

- Jobs remain `BackgroundService` classes (V1 pattern), each creating a scope per run.
- **Only one instance runs each job:** a SQL Server application lock (`sp_getapplock`) per job name, taken at the start of each run (fixes the M9 multi-instance note). No new infrastructure is needed.
- If job needs grow (retries, dashboards, cron expressions), switch to Hangfire on the same SQL Server; recorded as an open decision ([10 §5](10_ROADMAP.md#5-open-questions)).
- Integration tests remove all jobs, as V1's `ClinicFlowApiFactory` removes `AppointmentReminderJob`.

## 8. Files and documents

- `IFileStorage`: `PutAsync`, `OpenReadAsync`, `DeleteAsync`. Implementations: local disk (Development), Azure Blob Storage or an S3-compatible store (Production; decision in [10 §5](10_ROADMAP.md#5-open-questions)). Files are never stored in SQL Server.
- Uploads go through the API: size and type checks (PDF, JPEG, PNG, HEIC, DICOM excluded in V2), SHA-256 hash, **virus scan** (ClamAV container) before the attachment becomes visible (`ScanStatus`).
- Downloads go through an API endpoint that checks permissions, writes a read audit entry, and streams the file. No public URLs.
- `IPdfRenderer` for invoices, receipts, prescriptions, referral forms and statements. It needs Arabic (RTL) text shaping; QuestPDF is the leading candidate (check its licence against the clinic's revenue), with a fallback of HTML → PDF via headless Chromium.
- Every generated document carries a **verification code** (random, stored with the document) so a pharmacy or insurer can check authenticity at a public verification page that shows only minimal data.

## 9. Integrations

### 9.1 Payments

- `IPaymentGateway`: `CreatePaymentAsync(amount, reference, returnUrl)` → redirect URL or iframe token; `VerifyWebhook(request)`; `RefundAsync(transactionId, amount)`.
- Card data never touches ClinicFlow: hosted payment page or iframe, so the PCI DSS scope stays at SAQ A.
- Webhooks are the source of truth: `POST /api/v2/payments/webhooks/{provider}` (anonymous + signature check), idempotent on the provider transaction id. The return URL only shows the result.
- Egyptian candidates: Paymob, Fawry, Kashier. The choice is an open business decision.

### 9.2 SMS and WhatsApp

- `IMessageSender` per channel, chosen per message type by configuration. WhatsApp Business requires pre-approved templates, so `MessageTemplate` keeps the provider template id.
- Patients' phone numbers are normalised to E.164 at registration (V1 validator rule).

### 9.3 Email

MailKit stays, moved into `Infrastructure` and called only from outbox handlers, with one SMTP connection per batch (NL6).

### 9.4 Real-time queue

- A SignalR hub `/hubs/queue`. Clients join group `branch:{id}` (checked against `queue.view`). Queue changes publish an in-process event that pushes a compact update.
- The waiting-room display authenticates with a long-lived **device token** limited to the display's branch, showing ticket numbers and rooms only.
- With more than one API instance, SignalR needs a backplane (Redis or Azure SignalR). V2 starts single-instance; the backplane is added when scaling out.

## 10. Data access and performance

- **Reads:** `AsNoTracking()` and `ProjectTo<T>()` for every list (NL1, NM4); split read and write repository methods where V1 shares them.
- **Paging:** one helper `ToPagedResponseAsync(pageNumber, pageSize)` with defaults (1/20) and a max page size (100). Fixes H6 and NL2.
- **Patient search:** a persisted computed column `SearchName` (normalised Arabic and Latin), indexed; phone search on a normalised digits column. Full-text search is considered if search gets slow at 200k patients.
- **Reports:** reporting tables (e.g. `DailyBranchStats`, `DoctorUtilisation`) refreshed by `ReportingRefreshJob` from the transactional tables; dashboards never run heavy aggregates on hot tables. Live numbers (queue length, today's bookings) are direct indexed queries.
- **Indexes** planned with each migration: every foreign key; `(BranchId, Date)` on slots and appointments; `(PatientRecordId, CreatedAt)` on encounters; `(Status, BranchId)` on invoices; `(OrganizationId, Mrn)` unique.
- **Retry on transient faults:** `EnableRetryOnFailure` with manual transactions wrapped in `Database.CreateExecutionStrategy().ExecuteAsync(...)` (NL14). The `IUnitOfWork` transaction helper does this, so services don't change.

## 11. Concurrency and idempotency

| Risk | Mechanism |
|---|---|
| Two bookings for one slot | Conditional `UPDATE ... WHERE BookedCount < Capacity` in the booking transaction (the V1 pattern, generalised) |
| Two staff editing the same encounter, appointment or invoice | `RowVersion` → `409 Concurrency.Conflict`; the client reloads |
| Double-click / retry on payment or booking | `Idempotency-Key` header on `POST` payments, refunds and bookings; `IdempotencyRecord(key, userId, requestHash, responseJson, expiresAt)`; the same key returns the stored response |
| Invoice number gaps or duplicates | `UPDATE InvoiceNumberSequence SET LastNumber = LastNumber + 1 OUTPUT inserted.LastNumber WHERE BranchId = @b AND Year = @y` inside the issuing transaction |
| Gateway webhook delivered twice | Unique index on `Payment.GatewayTransactionId` |
| Job running twice | `sp_getapplock` (§7) |

## 12. Testing strategy

| Level | What | Tooling |
|---|---|---|
| Unit | State machines (every legal and illegal transition), price resolution, coverage split, invoice totals, discount limits, slot generation including DST, `IAccessGuard` against the default matrix | xUnit + NSubstitute (V1 conventions) |
| Integration | Every endpoint: success, validation, not-found, and **permission matrix tests** generated from [02 §6](02_ROLES_AND_PERMISSIONS.md#6-role--permission-matrix) (each role × permission × in-scope / out-of-scope branch) | WebApplicationFactory + Testcontainers SQL Server (V1); `TestAuthHandler` gains `X-Test-Org`, `X-Test-Staff`, `X-Test-Branches` headers |
| Tenancy | A second organization is seeded in every integration test class; tests assert its data never appears | Integration |
| Outbox | Events written with the business change; handler idempotency; retry and dead-letter | Integration |
| Contract | `openapi.json` regenerated and diffed in CI; breaking changes fail unless the contract version is bumped | CI script |
| Load | Booking burst on one slot (exactly one wins), queue board with 50 clients, search at 200k patients | k6 |
| Migration | V1 → V2 data migration on a copy of production data, with row-count and checksum reports | Script + integration ([09](09_MIGRATION_FROM_V1.md)) |

CI (GitHub Actions; V1 has none, REVIEW §4.10): build with warnings as errors, unit tests, integration tests (Docker service), openapi diff, `dotnet list package --vulnerable`.

## 13. Errors and API shapes

- `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`: every error, including 401, 403, 404 and unhandled 500s, returns ProblemDetails with `traceId` (M5, NL9).
- `ValidationFilter` builds its response with `ProblemDetailsFactory`, so validation errors have `type`, `status` and `traceId`.
- `ErrorType` mapping is unchanged, but the catalog is corrected: conflicts → `Conflict` (409), bad tokens and business-rule input → `Validation` (400). `Failure` means an unexpected server error only (M3).
- Error codes are listed per module in the V2 API contract, generated from `Shared/Errors` to avoid drift.

## 14. Operations

- **Migrations move out of startup** into a deploy step (`dotnet ef migrations bundle`), so several instances can start safely (REVIEW §4.9). Startup only checks that the database is at the expected migration.
- **Health checks:** `/health/live` and `/health/ready` (database, outbox lag, storage, provider reachability) (M12).
- **Metrics:** queue length per branch, median wait, outbox lag, job duration and failures, payment webhook failures, login failures.
- **Logs:** Serilog as V1, with a destructuring policy that masks phone numbers, national ids and clinical text; no request or response bodies on clinical or payment endpoints.
- **Environments:** Development (demo seed), Staging (anonymised copy of production for UAT), Production.
- **Backups:** per [04 NFR-04](04_FUNCTIONAL_REQUIREMENTS.md#5-non-functional-requirements); file storage is backed up with the same retention.
