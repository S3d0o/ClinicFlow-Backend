# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

.NET 10 solution (`ClinicFlow.slnx`). Run everything from the repo root.

- Build: `dotnet build ClinicFlow.slnx`. Add `--no-incremental` to see every warning, because incremental builds hide warnings from projects that are already up to date. The build has 0 warnings, and `Directory.Build.props` makes nullable warnings errors.
- Run the API: `dotnet run --project ClinicFlow/ClinicFlow.csproj`. Use `--launch-profile https` for https://localhost:7129; the default http profile is http://localhost:5143. Scalar docs are at `/scalar` in Development.
- Unit tests: `dotnet test ClinicFlow.Tests`
- Integration tests: `dotnet test ClinicFlow.IntegrationTests`. **Docker must be running**, because Testcontainers starts a SQL Server 2022 container.
- Single test or class: `dotnet test ClinicFlow.Tests --filter "FullyQualifiedName~BookAppointmentAsyncTests"` (or `~ClassName.MethodName`)
- Docker image: `docker build -t clinicflow-backend-v2 .` (the Dockerfile is at the repo root). `docker compose -p clinicflow-v2 up` runs the V2 stack (always pass `-p`: the shared `.env` may set `COMPOSE_PROJECT_NAME=clinicflow`, V1's project, which overrides the file's `name:`) (compose project `clinicflow-v2`): db on `127.0.0.1:1434`, backend on 5002, frontend on 3001, so it never collides with V1's stack (1433, 5001, 3000).
- Packages: `dotnet list ClinicFlow.slnx package --vulnerable --include-transitive` and `dotnet list ClinicFlow.slnx package --outdated`

CI: `.github/workflows/v2.yml` (V2 line only) runs on pushes to `v2` and to `feature/**`, `fix/**`, `docs/**`, `test/**` and `chore/**` branches, and on PRs into `v2`. It runs:
- a Release build with warnings as errors
- unit tests
- integration tests (Testcontainers on the runner)
- a vulnerable-package check that fails the run

Keep it green before merging into `v2`. No linter is configured.

## Migrations

`dotnet-ef` is not installed as a local tool. Install it once with `dotnet tool install --global dotnet-ef`.

- Add: `dotnet ef migrations add <PascalCaseName> --project Persistance/Persistence.csproj --startup-project ClinicFlow --output-dir AppData/Migrations`
- Apply manually: `dotnet ef database update --project Persistance/Persistence.csproj --startup-project ClinicFlow`
- Remove the last unapplied migration: `dotnet ef migrations remove --project Persistance/Persistence.csproj --startup-project ClinicFlow`
- On startup, `DataSeeder.SeedAsync` (`ClinicFlow/Program.cs`) calls `DataSeeder.EnsureSchemaAsync`:
  - When `Database:MigrateOnStartup` is true (the default in Development, and set by the test factory), it runs `Database.MigrateAsync()`, so `dotnet run` applies pending migrations by itself.
  - Otherwise (Docker, production; docs/v2/plans/P0.md D1) it only checks that no migration is pending, and throws naming them if any is. The migration bundle (`/app/efbundle` in the image, run by compose's `migrate` service as the deploy login) applies them first.
  - Either way it retries on `SqlException` for about a minute, because SQL Server accepts connections before it has recovered existing databases and EF then fails with "database already exists" (error 1801). It then seeds roles and specialties, an optional bootstrap admin, and (only when `Seeding:DemoData` is true) the demo dataset.
- The demo dataset has two parts: hand-written accounts with fixed stories (`DataSeeder.cs`) and a generated clinic (`DataSeeder.LargeDemo.cs`: 53 more doctors, 75 more patients, about 2,450 appointments over 60 days of history and 21 ahead, about 900 reviews and 7,000 notifications, from a fixed random seed, so every fresh database is identical). Each part is created once, with dates relative to the clinic's clock on the day it is seeded; demo slots are topped up on every start (60 days back to 21 days ahead), so an old demo database stays bookable, but its appointments keep their original dates. To get fresh demo data, drop the database and restart (a fresh seed takes about 30 s). Seeded upcoming visits are marked as already reminded, so `AppointmentReminderJob` never emails demo addresses. Integration tests switch demo seeding off (`ClinicFlowApiFactory`).
- Name migrations in PascalCase without spaces. A name with spaces once produced an all-lower-case class and warning CS8981 (the class has since been renamed; the migration id is what EF uses).
- `dotnet ef` builds and starts the host, so the configuration below must be present.

## Configuration and secrets

Nothing secret lives in `appsettings.json`, and nothing secret should be added to it. Supply secrets from one of these sources:
- user secrets (`UserSecretsId` is set in `ClinicFlow/ClinicFlow.csproj`; V2 has its own id, so its secrets are separate from V1's)
- the git-ignored `ClinicFlow/appsettings.Development.json`. It is **shared by both branches** because it lives in the working tree, and it normally points at V1's `ClinicFlow` database. Put V2's connection string in V2's user secrets, which override the file.
- environment variables, using `__` as the separator (e.g. `JwtSettings__Secret`)

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Yes | Startup throws without it, and also when it names any database other than `Database:ExpectedName` (`InfraStructureServiceExtensions.cs`). That check also stops `dotnet ef` from migrating V1's database. Integration tests don't need it: `ClinicFlowApiFactory` sets it, with the expected name and a JWT secret, to the Testcontainers database `ClinicFlowV2TestDb` |
| `Database:ExpectedName` | No | Default `ClinicFlowV2` (in `appsettings.json`). The only database V2 will start against (`docs/v2/adr/0007-v2-separate-product-line.md`) |
| `Database:MigrateOnStartup` | No | Default: `true` in Development, `false` otherwise. When false, startup refuses a database with pending migrations instead of applying them (see Migrations). Never `true` in production: the app's login can't run DDL there |
| `JwtSettings:Secret` | Yes | HMAC-SHA256 key, at least 32 bytes. **Validated at startup** (`ValidateOnStart`), along with issuer, audience and lifetimes, which live in `appsettings.json` |
| `EmailSettings:Username`, `EmailSettings:Password`, `EmailSettings:FromAddress` | For email | SMTP (Gmail, port 587, StartTLS). Host, port and from-name are in `appsettings.json` |
| `Frontend:BaseUrl` | No | Default `http://localhost:3000`. Used for the CORS origin and for links in emails |
| `RateLimiting:Auth` | No | `PermitLimit` / `WindowSeconds`, default 10 / 60 (in `appsettings.json`): per-client-IP limit on the credential and email endpoints (`RateLimitPolicies.Auth`). Integration tests lift it in `ClinicFlowApiFactory` |
| `Clinic:TimeZoneId` | No | IANA zone of the clinic, default `Africa/Cairo` (in `appsettings.json`). Slot dates and times are wall-clock times in this zone. **Validated at startup** |
| `Seeding:DemoData` | No | Seeds the demo accounts with published passwords. Default: `true` in Development, `false` otherwise. Never enable in production |
| `Platform:AllowPlanSwitching` | No | Lets the clinic admin switch the organization's plan and add-ons (`/api/v2/platform/subscription`, until the operator console in R4). Default: `true` in Development, `false` otherwise. Demo deployments only: it upgrades a clinic for free |
| `Seeding:AdminEmail`, `Seeding:AdminPassword` | No | When both are set, an admin with these credentials is created on startup if one doesn't exist |

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=ClinicFlowV2;User Id=sa;Password=<pwd>;TrustServerCertificate=True" --project ClinicFlow
dotnet user-secrets set "JwtSettings:Secret" "<at-least-32-byte-random-string>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Username" "<smtp user>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Password" "<smtp app password>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:FromAddress" "<from address>" --project ClinicFlow
```

- `docker-compose.yml` reads `DB_PASSWORD`, `APP_DB_PASSWORD`, `DEPLOY_DB_PASSWORD`, `JWT_SECRET`, `EMAIL_USERNAME`, `EMAIL_PASSWORD`, `EMAIL_FROM_ADDRESS` and optionally `ADMIN_EMAIL` / `ADMIN_PASSWORD` and `SEED_DEMO_DATA` from `.env` (git-ignored, also excluded from the Docker build context). It runs with `ASPNETCORE_ENVIRONMENT=Production`, so it creates no demo data unless `SEED_DEMO_DATA=true` (local demos only). SQL Server is published on `127.0.0.1:1434` only (V1's stack uses 1433), with its own volume `clinicflow_v2_data`. Start order: `db` → `db-init` → `migrate` → `backend`. The one-shot `db-init` service runs `docker/db-init.sql` as `sa` (names come from `APP_DATABASE` / `APP_LOGIN` / `APP_DEPLOY_LOGIN`). It creates two logins:
  - `clinicflow_v2_deploy`, `db_owner` of `ClinicFlowV2`, which the one-shot `migrate` service uses to run the migration bundle.
  - `clinicflow_v2_app`, which the backend connects as. It is a member of the `clinicflow_app` role, whose rights come from the `AddAppDatabaseRole` migration: read and write, no DDL, no `UPDATE`/`DELETE` on `AuditEntries` (`AppDatabaseRoleTests`).

  Only `db` and `db-init` use `sa`. Both stacks read the same `.env`.
- Health probes (`ClinicFlow/Extensions/HealthCheckExtensions.cs`, all anonymous): `/health/live` (used by the Dockerfile `HEALTHCHECK`; `/health` is an alias kept from V1) checks nothing but the process; `/health/ready` checks the database (503 when down) and the outbox lag (`Degraded`, still 200, when the oldest due message has waited over a minute).
- Serilog writes to the console, `logs/log-.txt` and Seq at `http://localhost:5341`. Seq is optional; if it isn't running, that sink simply drops events.
- Client IPs come from `Connection.RemoteIpAddress`. `X-Forwarded-For` is applied by `UseForwardedHeaders`, which trusts loopback only; add the proxy to `ForwardedHeadersOptions.KnownProxies` (`WebApiExtensions.cs`) when deploying behind one.
- Demo logins (Development, or `Seeding:DemoData=true`) are listed in the doc comment at the top of `Persistance/AppData/DataSeeder.cs`, e.g. `admin@clinicflow.com / Admin@1234`. Generated accounts are `doctor01`…`doctor53@clinicflow.test` (`Doctor@1234`; 49–53 are pending approval, 46–48 suspended) and `patient01`…`patient75@clinicflow.test` (`Patient@1234`).

## Solution layout

Clean Architecture, one project per layer. Folders are listed per project, and each project's `Global.cs` already imports the namespaces shown in the last column. Check it before adding usings. In Persistence and Services.Abstraction the file is named `Global.cs.cs`.

| Project (folder) | Contents | `Global.cs` imports |
|---|---|---|
| `Domain` | `Entities/AppModule` (doctor, patient, schedule, slot, appointment, review, specialty), `Entities/IdentityModule` (`ApplicationUser`, `Notification`, `RefreshToken`), `Entities/Tenancy` (`Organization`, `Branch`, `IOrganizationOwned`, `IBranchOwned`), `Time/WallClock.cs` (wall clock → UTC with the daylight saving rules), `Events/` (`IDomainEvent`, the event records), `Entities/Messaging` (`OutboxMessage`, `MessageDelivery`), `Entities/Auditing` (`AuditEntry`), `Entities/Subscriptions` (`Plan`, `PlanModule`, `PlanLimit`, `OrganizationSubscription`, `OrganizationModuleOverride`: docs/v2/adr/0008), `Interfaces/IOutbox.cs`, `ResultPattern/` (`Result`, `Result<T>`, `Error`, `ErrorType`; here so entities can return them), `Errors/AppointmentErrors.cs` (the errors an entity returns), `Enums`, `Interfaces/IUnitOfWork.cs`, `Interfaces/ITenantContext.cs`, `Interfaces/IRepositories/I*Repo.cs`, `Parameters/*FilterParams.cs` | `Domain.Enums`, `Domain.Entities.AppModule`, `Domain.Entities.Tenancy`, `Domain.Parameters`, `Domain.ResultPattern`, `Domain.Errors`, `Domain.Events`, `Shared.Enums`, Identity |
| `Shared` | `DTOs/<Feature>/` request/response records, `Errors/<Feature>Errors.cs` (except `AppointmentErrors`, in Domain), `Authorization/` (`Roles`, `Permissions` (the 02 §5 catalog), `PermissionScope`, `DefaultPermissionMatrix` (02 §6), `Modules` (the module catalog, dependencies and the module of each permission, ADR 0008), `Limits`), `Settings/` (`JwtSettings`, `EmailSettings`, `FrontendSettings`, `ClinicSettings`), `DTOs/Common/PagedList.cs` | none |
| `Persistance` (project `Persistence.csproj`, namespaces `Persistence.*`) | `AppData/ClinicDbContext.cs`, `AppData/DataSeeder.cs`, `AppData/Configurations/<Entity>Config.cs`, `AppData/Migrations/`, `Repositories/<Name>Repo.cs`, `Queries/<Module>Queries.cs` (the CQRS-lite reads, e.g. `AppointmentQueries`), `Transactions/` (the retrying transaction runner), `Tenancy/` (`SingleOrganizationTenantContext`, `TenantSaveChangesInterceptor`), `Outbox/` (`OutboxSaveChangesInterceptor`, `OutboxProcessor`, `Outbox`), `Auditing/` (`AuditSaveChangesInterceptor`, the `IsAudited` configuration extensions), `Subscriptions/` (`EntitlementChanges`: the save and commit interceptors that invalidate the entitlement cache), `UnitOfWork.cs` | EF Core, `Persistence.AppData`, `Domain.Interfaces.IRepositories`, `Domain.ResultPattern`, `Shared.Authorization`, `Domain.Entities.Messaging`, `Domain.Entities.Auditing`, `Persistence.Auditing`, entities |
| `Services.Abstraction` | `Contracts/I<Name>Service.cs` and `I<Module>Queries.cs`, plus `IEmailService`, `ITokenService`, `IAccessTokenFactory`, `IClock`, `ICurrentUser`, `IAccessGuard` | `Shared.DTOs`, `Domain.ResultPattern` |
| `Services` | `Implementations/<Name>Service.cs`, `EventHandlers/<Event>Handler.cs` (outbox handlers, registered by assembly scan), `MappingProfiles/<Feature>Profile.cs` (AutoMapper), `Extensions/CoreServiceExtensions.cs` (DI), `ServiceAssemblyReference.cs` (assembly marker) | AutoMapper, logging, `Services.Abstraction.Contracts`, `Shared.Errors`, `Domain.Errors`, `Domain.ResultPattern`, `Domain.Interfaces`, `Domain.Events`, `Shared.Authorization` |
| `Infrastructure` | Adapters for outside services behind `Services.Abstraction` interfaces: `Email/EmailService.cs` (MailKit), `Auth/JwtAccessTokenFactory.cs` (JWT creation), `Extensions/ProviderServiceExtensions.cs` (`AddProviderServices`: DI and their settings) | logging, `Services.Abstraction.Contracts` |
| `Presentation` | `Controllers/<Name>Controller.cs` + `ApiController.cs` (abstract base), `Validators/<Request>Validator.cs`, `Filters/ValidationFilter.cs`, `Extensions/HttpContextExtensions.cs`, `Authorization/` (`[HasPermission]`, `PermissionPolicyProvider`, `PermissionAuthorizationHandler` (role grant **and** module enabled), `[RequiresModule]`, `SubscriptionAuthorizationResultHandler` (403 `Subscription.ModuleNotEnabled`), `HttpCurrentUser`) | MVC, `FluentValidation`, `Domain.ResultPattern`, `Shared.Authorization`, `Shared.DTOs.Doctor`/`Patient` |
| `ClinicFlow` (host) | `Program.cs`, `Extensions/WebApiExtensions.cs` (MVC, filters, validators), `Extensions/InfraStructureServiceExtensions.cs` (DbContext, Identity, JWT, `IUnitOfWork`, the query classes), `Extensions/HealthCheckExtensions.cs` + `HealthChecks/` (the probes), `BackgroundJobs/AppointmentReminderJob.cs` (hourly, queues reminders), `BackgroundJobs/SlotGenerationJob.cs` (daily, keeps slots 14 days ahead) and `BackgroundJobs/OutboxDispatcher.cs` (every 2 s, delivers the outbox under a SQL application lock), all `BackgroundService` | Serilog, `ClinicFlow.Extensions`, `Services.Extensions` |
| `ClinicFlow.Tests` | `Services/<Service>Tests/` with a `*TestsBase` and one class per method; `Domain/` for entity rules (`Domain/Appointments/`: every status transition); `Infrastructure/` for the adapters; `Architecture/LayerDependencyTests.cs` | |
| `ClinicFlow.IntegrationTests` | `Infrastructure/` (`ClinicFlowApiFactory`, `TestAuthHandler`, `SeedDataHelper`, `IntegrationTestCollection`), `Tests/<Feature>/` | |

Project references: `Shared → Domain`, `Persistence → Domain, Services.Abstraction` (its query classes implement read interfaces declared there), `Services.Abstraction → Shared`, `Services → Domain, Services.Abstraction`, `Infrastructure → Services.Abstraction`, `Presentation → Services.Abstraction`, `ClinicFlow → Infrastructure, Persistence, Presentation, Services`.

**The layer rules are tested** (`ClinicFlow.Tests/Architecture/LayerDependencyTests.cs`, docs/v2/06 §2). A layer that uses a type it mustn't fails the unit tests, even when the compiler allows it through a transitive reference. Presentation may use Domain enums and the Result types (`Domain.ResultPattern`) but no other Domain type. Services may not use EF Core, MailKit/MimeKit or the JWT libraries either. `KnownExceptions` is empty since P0 PR 3; never add an entry to get a build through.

`Program.cs` calls `AddWebApiServices` / `AddInfraStructureServices` / `AddCoreServices` / `AddProviderServices`, registers the background jobs and Serilog, and maps ProblemDetails for every error (`UseExceptionHandler`, `UseStatusCodePages`), and ends with `public partial class Program` so tests can use `WebApplicationFactory<Program>`.

### Namespace gotchas
Enum namespaces don't match their folder:
- `AppointmentStatus`, `NotificationType` and `DoctorSortBy` are in `Domain.Enums`.
- `SlotStatus`, `CancelledBy` and `BloodType` are in `ClinicFlow.Domain.Enums`.
- `Gender` is in `Shared.Enums`.

Files often need several of these usings. `IReviewRepo` is declared in namespace `Persistence.Repositories` even though it lives in Domain.

## Conventions

### Git workflow
- **Two product lines** (`docs/v2/adr/0007-v2-separate-product-line.md`):
  - **`master` is V1.** V1 is finished and stays untouched. Change it only when the owner explicitly asks for a V1 fix.
  - **`v2` is V2.** It has its own database, `ClinicFlowV2`; never point V2 at V1's database or run V2 migrations against it.
  - **Separation is enforced** (P0 PR 0): `v2` has its own `UserSecretsId`, and startup refuses any database but `ClinicFlowV2` (`Database:ExpectedName`). Never weaken that check or point `ExpectedName` at V1's database.
  - **V1 fixes:** when one lands on `master`, check whether it applies to `v2` and cherry-pick it.
- **Every new V2 feature gets its own branch off `v2`.** Create the branch before writing any code: `git switch v2 && git pull && git switch -c feature/r<release>-<kebab-case-name>` (since 2026-10-29 V2 ships as releases, `docs/v2/plans/R1.md`; P0's branches used `feature/p<phase>-…`). Merge it back into `v2`. Releases are tags (`v2.0.0-r<n>`), not branches; cut `release/r<n>` from a tag only for a live fix while unfinished work is on `v2`, and merge the fix back (`docs/v2/plans/R1.md` §1). Never build a feature on `v2`, `master` or another feature's branch.
- **V1 fixes**, only when asked: branch off `master` as `fix/<name>`.
- **Uncommitted changes:** if the working tree has them when a feature starts, stop and ask whether to commit, stash or carry them over. Don't switch branches over them.
- **Other branch prefixes:** `fix/`, `docs/`, `test/` and `chore/` follow the same rule for other kinds of work (e.g. `docs/review-and-claude-md`). They branch off `v2` for V2 work and off `master` for V1 work.
- **Commits and pushes:** commit or push only when asked.

### Naming
- **Repositories:** `I<Name>Repo` / `<Name>Repo`. **Services:** `I<Name>Service` / `<Name>Service`.
- **Persistence and mapping:** `<Entity>Config : IEntityTypeConfiguration<T>` for EF configs, `<Feature>Profile : Profile` for AutoMapper.
- **Errors and validation:** `<Feature>Errors` (static class), `<Request>Validator : AbstractValidator<TRequest>`.
- **DTOs:** records named `*Request` / `*Response`. List filters are `*FilterRequest` (DTO), mapped to `Domain/Parameters/*FilterParams`.
- **Async methods:** end in `Async` and take `CancellationToken ct` as the **last** parameter, passed down to every EF call.
- **Constructors:** services, repositories, controllers and jobs use primary constructors (`public class XService(IUnitOfWork uow, IMapper mapper, ILogger<XService> logger) : IXService`).
- **Logging:** structured Serilog templates with named placeholders (`"... {AppointmentId}"`), never string interpolation. Log a warning before returning an expected failure.

### Design (V2 and enhancements)
- **Delivery by release.** V2 ships as releases R1–R4 (`docs/v2/10_ROADMAP.md` §1). The current one is **R1 "Clinic day"** (`docs/v2/plans/R1.md`): the core modules with plans and modules (ADR 0008, accepted), front desk, triage, billing at the desk and online booking. Work outside the current release (EMR, recruitment, MFA, files, Paymob, insurance, reports exports, the chatbot) waits for its release unless the owner says otherwise.
- **SOLID and clean code.** Follow `docs/v2/06_TECHNICAL_DESIGN.md` §2.1 and `docs/v2/adr/0006-clean-architecture-modular-monolith.md`. Write small use-case services, keep status rules in entity methods, use small interfaces, and keep controllers thin.
- **CQRS-lite.** Inside a module, keep command services separate from query services:
  - **Command services** load tracked entities through `IUnitOfWork`, call entity methods, then save.
  - **Query services** use `AsNoTracking` and `Select` projections straight to response DTOs, and never write. Their interfaces live in `Services.Abstraction` and are implemented in `Persistence`.
  - Use one database. Don't add MediatR or a separate read store.
  - Example (P0 PR 7): appointments have `AppointmentBookingService`, `AppointmentCancellationService` and `AppointmentVisitService` for commands, and `IAppointmentQueries` / `Persistance/Queries/AppointmentQueries.cs` for reads. A query class does its own not-found and ownership checks and returns `Result`, like a service.
- **Status rules belong to the entity** (docs/v2/adr/0004). `Appointment.Status`, and what each transition records, have private setters. Change them only through the entity's methods (`Book`, `CheckIn`, `Start`, `Complete`, `Cancel`, `MarkNoShow`, `RevertNoShow`, `RescheduleTo`, `AddDoctorNotes`), which take `now` (and the actor where it matters) and return `Result`. The service handles who is asking, the slot and saving. Test every legal and illegal move in `ClinicFlow.Tests/Domain/Appointments`.
  - **Stored history isn't an action:** the demo seeder walks the transitions (so its data obeys the rules), but tests that need a row in a given status write it through EF (`db.Appointments.Add(a).Property(x => x.Status).CurrentValue = status`), and unit tests use `appointment.WithStatus(status)` (`ClinicFlow.Tests/Services/AppointmentTestExtensions.cs`).
- **Patterns on merit.** Adopt a pattern (CQRS-lite, strategy, specification, domain events, …) when it makes the code clearer and the trade-off is worth it. That applies to new code **and** to enhancing existing V1 code. Don't add patterns that only add ceremony. Record a structural adoption in `docs/v2/10_ROADMAP.md` §6.

### Errors and results
- Services never throw for expected failures. They return `Result` / `Result<T>`. Return an `Error` or a value directly; implicit conversions do the wrapping. A `Result` can't mix `Validation` errors with other types; doing so throws.
- In `Shared/Errors/<Feature>Errors.cs` (or `Domain/Errors/` for errors an entity returns, such as `AppointmentErrors`; Domain can't see Shared), declare fixed errors as `public static readonly Error X = Error.Conflict("Feature.Reason", "...")` and parameterized ones as `public static Error NotFound(int id) => Error.NotFound(...)`. Codes are `Feature.Reason`. Name the C# member after the error type: the 403 ownership errors are `AppointmentErrors.Forbidden`, `ReviewErrors.Forbidden` and `NotificationErrors.Forbidden`, though their codes keep the contract names `*.Unauthorized`.
- Pick the `ErrorType` for the status code it produces: NotFound→404, Conflict→409, Forbidden→403, Validation→400, Unauthorized/InvalidCredentials→401, Failure→500. Use `Failure` only for unexpected server errors; every expected error was moved off it in contract 2.0.0 (`docs/REVIEW.md` M3). Integration tests assert status codes, so changing a type changes the API.
- **Notifications and emails go through the outbox** (docs/v2/06 §5, P0 PR 9), never inline. An entity method raises a domain event (`Appointment`'s transitions: `Domain/Events`), or the service adds one with `uow.Outbox.Add(new X(...))` when no entity raises it (a request with no entity change, or a row just inserted: add it after that insert, inside the same transaction). Either way it is saved as an `OutboxMessage` in the same transaction as the change, so it goes out if, and only if, the change commits, and a retried transaction can't send it twice. The `OutboxDispatcher` job delivers it about 2 s later by calling each `IDomainEventHandler<TEvent>` in `Services/EventHandlers` (registered automatically). Delivery is at least once with retries, backoff and a dead letter, so a handler sends each channel through `Delivery.OnceAsync` (it checks `uow.MessageDeliveries`). A handler that fails throws; the outbox retries it. Create email tokens in the handler, never in the event, so no secret is stored. The demo seeder sets `ClinicDbContext.RecordDomainEvents = false` (and `RecordAudit = false`).
- **"Now" comes only from the injected `IClock`** (REVIEW.md §4.1; `Clock` wraps `TimeProvider.System`):
  - Never read `DateTime.UtcNow`, `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` or `TimeProvider.System` in production code. `ClockUsageTests` scans every production project, the host included, and fails on them.
  - Slots have two forms of the same times (docs/v2/adr/0003): `Date` / `StartTime` / `EndTime` are the **branch's wall clock** (what people see), and `StartsAtUtc` / `EndsAtUtc` are the instants. **Every rule that compares a slot with "now" uses `StartsAtUtc` and `UtcNow`** (the past-slot check, the 2-hour cancellation window, completion, no-show grace, reminders).
  - Calendar questions about slots ("today", the generation window) use the branch's zone: `clock.Today(await uow.Branches.GetTimeZoneIdAsync(branchId, ct))`. New slots get their instants from `clock.ToUtc(zone, date, time)` (`Domain.Time.WallClock`), which returns null for a time daylight saving skips (no slot is made) and picks the first occurrence of a repeated time. `ClinicNow` / `ClinicToday` remain only for the configured clinic zone (date-of-birth rules, the seeder).
  - Use `UtcNow` for stored timestamps such as `CancelledAt`.
  - Entities have no `= DateTime.UtcNow` defaults: whoever creates one sets `BookedAt` / `CreatedAt` from the clock. Repositories and entities don't read the clock either; pass "today" or "now" in (`docs/REVIEW.md` M2, `RefreshToken.IsActiveAt(utcNow)`).
  - Every `DateTime` column holds UTC and is read back as `DateTimeKind.Utc` (`UtcDateTimeConverter`), so the JSON carries `Z`. Clinic-local values are `DateOnly` / `TimeOnly`, never `DateTime` columns.
  - Unit tests stub `IClock`; integration tests seed time-sensitive slots from `_factory.ClinicNow` and give every seeded slot `StartsAtUtc` / `EndsAtUtc` from `SeedDataHelper.SlotUtc(date, time)`.

### Endpoints
- Controllers live in `Presentation/Controllers`, inherit `ApiController` (not `ControllerBase`), take the service through a primary constructor, and set an explicit route: `[Route("api/appointments")]`. The base `api/[controller]` route is only used by `SpecialtyController`.
- Every action has:
  - `[EndpointSummary]`, `[EndpointDescription]`, and `[ProducesResponseType]` per status code (`ProblemDetails` / `ValidationProblemDetails` for errors)
  - `CancellationToken ct` as the last parameter
  - a body of `return HandleResult(await service.XAsync(..., ct));`
- `HandleResult(Result)` returns 204 by default. `HandleResult<T>(Result<T>)` returns 200, or 201 when given a location URI or a status code. For a paged list, return `Result<PagedList<T>>` from the service and use `HandlePagedResult`: the body stays an array and the total goes in the `X-Total-Count` header.
- Get the user id with `User.GetUserId()` and the role with `User.GetRole()` (`Presentation/Extensions/HttpContextExtensions.cs`). Roles are `Admin`, `Doctor` and `Patient` (until P1 adds staff); write them as `Roles.Admin` etc. (`Shared.Authorization`), never as string literals. Services that need the caller can inject `ICurrentUser` (user id, roles, IP) instead of taking them as parameters. For anything that reads or changes a specific record, check ownership in the service (see `AppointmentQueries.GetAppointmentAsync` and `ReviewService.DeleteReviewAsync`), not just the role.
- **Permissions, not roles, for new endpoints** (docs/v2/adr/0002): put `[HasPermission(Permissions.Appointments.CheckIn)]` on the action (403 unless one of the user's roles grants it in some scope, per `DefaultPermissionMatrix`), then ask `IAccessGuard.CanAccessAsync(permission, new AccessTarget(ownerUserId, branchId), ct)` in the service whether that scope covers the record, and return the feature's own Forbidden error when it doesn't. Inherited V1 endpoints keep their `[Authorize(Roles = ...)]` until they are replaced. A new permission goes into 02 §5–§6 and `Shared/Authorization` together (`PermissionMatrixTests` compares them).
- **Always add `[Authorize]` / `[Authorize(Roles = ...)]` / `[HasPermission]` or `[AllowAnonymous]` explicitly.** A fallback policy requires a logged-in user for anything without a rule, so a forgotten attribute fails closed, and `AuthorizationTests.EveryControllerAction_DeclaresAuthorizeOrAllowAnonymous` fails on it. Public endpoints need `[AllowAnonymous]` and an entry in `AuthorizationTests.AnonymousAllowlist`, which must match the app's public endpoints exactly.
- **The API is a contract shared with the frontend repo.** Any change to a route, request or response field, status code, error code or auth rule must update `docs/API_CONTRACT.md` (the endpoint section, the error catalog and the §10 changelog) and regenerate `docs/openapi.json` in the same change. Follow the process in §11 of that file. Breaking changes are listed in §9 first.
- Request bodies are validated automatically by the global async `ValidationFilter` (`ValidateAsync`, so `MustAsync` rules are fine). Add a `<Request>Validator` in `Presentation/Validators`; nothing else is needed.
- Enums are serialized as strings (`JsonStringEnumConverter`), and several are stored as strings in the DB.

### Registering things (new feature checklist)
1. **Entity:**
   - Put the entity in `Domain/Entities/AppModule` (or `IdentityModule`).
   - Add a `DbSet` property to `ClinicDbContext`.
   - **Tenancy:** anything the clinic creates is organization-owned, so implement `IOrganizationOwned`, plus `IBranchOwned` for operational records. `ClinicDbContext.ConfigureTenancy` then adds the query filter and the foreign keys, and `TenantSaveChangesInterceptor` fills in `OrganizationId`; set `BranchId` yourself (a slot takes its schedule's, an appointment its slot's). People and reference data (`ApplicationUser`, `PatientProfile`, `Specialty`) stay global. `TenancyModelTests` lists the expected types, so update it too (`docs/v2/adr/0001`).
   - Add `<Entity>Config` in `Persistance/AppData/Configurations`. It is picked up automatically by `ApplyConfigurationsFromAssembly`.
   - **Audit** (FR-AUD-01, docs/v2/06 §5.1): a clinical, financial or access-control record calls `builder.IsAudited("<Name>")` in its config. Mark clinical text `IsAuditRedacted()` (the entry only says it changed) and bookkeeping `IsNotAudited()`, then add the type to `AuditModelTests`. Its changes then write `AuditEntry` rows on save, with nothing to call from the service.
   - Add a migration.
2. **Repository:**
   - Put the interface in `Domain/Interfaces/IRepositories` and the implementation in `Persistance/Repositories` (primary constructor taking `ClinicDbContext context`).
   - Expose it as a property on `IUnitOfWork` and as a lazy `_x ??= new XRepo(_context)` property on `UnitOfWork`.
   - Repositories are **not** registered in DI. Services reach them only through `IUnitOfWork`.
   - **Query classes** (CQRS-lite reads) are the exception: the interface goes in `Services.Abstraction/Contracts`, the class in `Persistance/Queries` (taking `ClinicDbContext`), and it is registered with `services.AddScoped<IXQueries, XQueries>()` in `ClinicFlow/Extensions/InfraStructureServiceExtensions.cs`. Controllers inject it next to the command services. Test it with integration tests; it has no unit tests.
3. **Service:**
   - Put the interface in `Services.Abstraction/Contracts` and the implementation in `Services/Implementations`.
   - Register it **manually** with `services.AddScoped<IXService, XService>()` in `Services/Extensions/CoreServiceExtensions.cs`.
   - Services inject `IUnitOfWork`, never `ClinicDbContext`; `Services` doesn't reference `Persistence`, so the compiler enforces it. `AuthService`, `TokenService` and `ProfileService` also use Identity's `UserManager`.
4. **DTOs, mapping, validation:**
   - Put records in `Shared/DTOs/<Feature>/`.
   - Put the AutoMapper profile in `Services/MappingProfiles`. It is auto-registered by assembly scan of `ServiceAssemblyReference`.
   - Put the validator in `Presentation/Validators`. It is auto-registered via `AddValidatorsFromAssembly`.
5. **Errors:** add them to `Shared/Errors/<Feature>Errors.cs`, or `Domain/Errors/` when an entity method returns them.
6. **Controller:**
   - Add it to `Presentation/Controllers`. Controllers are discovered via `AddApplicationPart` in `WebApiExtensions.cs`.
7. **Infrastructure and background work:**
   - **Outside services** (email, and later SMS, payments, file storage): put a small interface in `Services.Abstraction/Contracts`, the adapter in `Infrastructure/<Area>/`, and register it and its settings in `Infrastructure/Extensions/ProviderServiceExtensions.cs`. Services depend on the interface only.
   - Host registrations (DbContext, Identity, auth, options) go in `ClinicFlow/Extensions/InfraStructureServiceExtensions.cs`. Options use `services.Configure<T>(config.GetSection(...))` / `AddOptions<T>().Bind(...)` with POCOs in `Shared/Settings`.
   - Background jobs are `BackgroundService` classes in `ClinicFlow/BackgroundJobs`, registered in `Program.cs` with `AddHostedService`. Each one creates a scope per run via `IServiceScopeFactory`.
   - Remove new jobs in `ClinicFlowApiFactory` so they don't run during integration tests.

### Data access
- **Double-booking guard:** `SlotRepo.SetStatusFromAvailableToBookedAsync` runs a conditional `ExecuteUpdate` (`WHERE Status = Available`) inside a transaction. If it affects 0 rows, the slot was already booked. `AppointmentSlot.RowVersion` is not what prevents double-booking.
- **Reads:** use `AsNoTracking()` on read-only queries.
- **Tenancy filters:** every query on an organization-owned entity is filtered to `ITenantContext.OrganizationId`, `ExecuteUpdate`/`ExecuteDelete` included. Use `IgnoreQueryFilters()` only on purpose (platform jobs, migrations, tests), and add the organization predicate yourself to raw SQL. Saving a row of another organization throws. In integration tests, seeded schedules, slots and appointments need `BranchId = Branch.MainId`; a second organization is seeded through its own `ClinicDbContext` (see `TenancyIsolationTests`).
- **Bulk writes:** use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (these need no `SaveChangesAsync`). They skip the save interceptors, so they write no audit entry: never use them on audited properties.
- **Audited inserts:** a new row of an audited type with an identity key (an appointment, a doctor profile) gets its audit entry from a second save once the database has given it an id. Insert it inside `ExecuteInTransactionAsync`, so the entry commits with it; outside a transaction the save throws. `AuditEntry` rows are append-only: saving a changed or deleted one throws.
- **Transactions:** `await uow.ExecuteInTransactionAsync(async tx => { ...; await tx.CommitAsync(ct); return value; }, ct)`. Returning without committing rolls back. It runs inside the SQL retry strategy (`EnableRetryOnFailure`, REVIEW.md NL14), so the operation may run more than once: commit as its last step. Emails and notifications go through the outbox, which a rolled-back attempt discards; raise the events inside the operation, so a retry raises them again. Between attempts the change tracker is rewound (`Persistance/Transactions/ChangeTrackerSnapshot.cs`). EF rejects any other manual transaction under the retry strategy; Persistence code such as the seeder uses `context.ExecuteInTransactionAsync` (`Persistence.Transactions`).
- **Optimistic concurrency:** `uow.TrySaveChangesAsync(ct)` returns `false` when a `RowVersion` changed since the entity was loaded (see `TokenService.RefreshTokenAsync`). A repository that must tell a unique-index race from other failures does it itself and returns a flag (`ReviewRepo.TryAddAsync`).

## Tests

- **Every service ships with its tests.** When you finish a new service, or change an existing one, write the tests in the same change:
  - **Unit tests:** one test class per public method, following the pattern below. Cover the success path, each expected error, and the edge cases.
  - **Integration tests:** add them where HTTP behaviour matters: status codes, authorization and ownership, validation, transactions and concurrency.
  - Run them. Don't call a service done until its tests pass.
- **Unit (`ClinicFlow.Tests`):** xUnit + NSubstitute. Each service gets a folder with a `*TestsBase` that substitutes every constructor dependency and builds `_sut` (see `Services/AppointmentBookingServiceTests/AppointmentBookingServiceTestsBase.cs`), plus one test class per method (`BookAppointmentAsyncTests`, ...).
- **Integration (`ClinicFlow.IntegrationTests`):** xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing`.
  - **Factory:** `ClinicFlowApiFactory` starts an MsSql Testcontainer, points `ClinicDbContext` at it, removes the background jobs (`AppointmentReminderJob`, `SlotGenerationJob`, `OutboxDispatcher`), and replaces JWT auth with `TestAuthHandler`. Call `await _factory.DrainOutboxAsync()` after the request under test before asserting emails (`_factory.Emails`) or notifications; pass a time to step past a retry's backoff.
  - **Authentication:** set the headers `TestAuthHandler.UserIdHeader` (`X-Test-UserId`) and `UserRoleHeader` (`X-Test-Role`) on a request to authenticate it. Without them the request is anonymous.
  - **Test classes:** every test class has `[Collection(nameof(IntegrationTestCollection))]`, so they share one container and run sequentially. Each one implements `IAsyncLifetime`. Its `InitializeAsync` calls `_factory.ResetDataBaseAsync()`, which drops and re-migrates the DB **without** the demo seed. It then clears the client headers and seeds its own data with `_factory.SeedAsync(db => SeedDataHelper...)`. Pass a distinct `suffix` to seed more than one set of entities. `SeedAsync` turns auditing off (`db.RecordAudit = false`), since seeded rows are stored history; a test of the audit itself sets it back to `true` (see `AuditTests`).
- **Naming:** test names follow `Method_Scenario_ExpectedResult`.

Known issues and their severities are tracked in `docs/REVIEW.md`. Check it before "fixing" something that looks wrong; it may be a recorded decision or have a noted fix risk.
