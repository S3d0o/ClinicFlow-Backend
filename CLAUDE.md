# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

.NET 10 solution (`ClinicFlow.slnx`). Run everything from the repo root.

- Build: `dotnet build ClinicFlow.slnx`. Add `--no-incremental` to see every warning, because incremental builds hide warnings from projects that are already up to date.
- Run the API: `dotnet run --project ClinicFlow/ClinicFlow.csproj`. Use `--launch-profile https` for https://localhost:7129; the default http profile is http://localhost:5143. Scalar docs are at `/scalar` in Development.
- Unit tests: `dotnet test ClinicFlow.Tests`
- Integration tests: `dotnet test ClinicFlow.IntegrationTests`. **Docker must be running**, because Testcontainers starts a SQL Server 2022 container.
- Single test or class: `dotnet test ClinicFlow.Tests --filter "FullyQualifiedName~BookAppointmentAsyncTests"` (or `~ClassName.MethodName`)
- Docker image: `docker build -t clinicflow-backend .` (the Dockerfile is at the repo root). `docker compose up` runs db + backend + frontend.
- Packages: `dotnet list ClinicFlow.slnx package --vulnerable --include-transitive` and `dotnet list ClinicFlow.slnx package --outdated`

No linter or CI workflow is configured.

## Migrations

`dotnet-ef` is not installed as a local tool. Install it once with `dotnet tool install --global dotnet-ef`.

- Add: `dotnet ef migrations add <PascalCaseName> --project Persistance/Persistence.csproj --startup-project ClinicFlow --output-dir AppData/Migrations`
- Apply manually: `dotnet ef database update --project Persistance/Persistence.csproj --startup-project ClinicFlow`
- Remove the last unapplied migration: `dotnet ef migrations remove --project Persistance/Persistence.csproj --startup-project ClinicFlow`
- On every startup, `DataSeeder.SeedAsync` (`ClinicFlow/Program.cs`) runs `Database.MigrateAsync()` and then seeds idempotent demo data, so `dotnet run` applies pending migrations by itself.
- Name migrations in PascalCase without spaces. A name with spaces produced the all-lower-case class `appointmentstatusisstringinthedb` and warning CS8981.
- `dotnet ef` builds and starts the host, so the configuration below must be present.

## Configuration and secrets

Nothing secret lives in `appsettings.json`, and nothing secret should be added to it. Supply secrets from one of these sources:
- user secrets (`UserSecretsId` is set in `ClinicFlow/ClinicFlow.csproj`)
- the git-ignored `ClinicFlow/appsettings.Development.json`
- environment variables, using `__` as the separator (e.g. `JwtSettings__Secret`)

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Yes | Startup throws without it (`InfraStructureServiceExtensions.cs`). Integration tests need it too, because `Program` registers services before the test factory swaps in the Testcontainers DbContext; any value works there |
| `JwtSettings:Secret` | Yes | HMAC-SHA256 key, at least 32 bytes. Issuer, audience and token lifetimes are in `appsettings.json` |
| `EmailSettings:Username`, `EmailSettings:Password`, `EmailSettings:FromAddress` | For email | SMTP (Gmail, port 587, StartTLS). Host, port and from-name are in `appsettings.json` |

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=ClinicFlow;User Id=sa;Password=<pwd>;TrustServerCertificate=True" --project ClinicFlow
dotnet user-secrets set "JwtSettings:Secret" "<at-least-32-byte-random-string>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Username" "<smtp user>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Password" "<smtp app password>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:FromAddress" "<from address>" --project ClinicFlow
```

- `docker-compose.yml` reads `DB_PASSWORD`, `JWT_SECRET`, `EMAIL_USERNAME` and `EMAIL_PASSWORD` from `.env` (git-ignored) and runs with `ASPNETCORE_ENVIRONMENT=Production`.
- Serilog writes to the console, `logs/log-.txt` and Seq at `http://localhost:5341`. Seq is optional; if it isn't running, that sink simply drops events.
- CORS (`Program.cs`) and the links in emails (`EmailService.cs`) are hardcoded to `http://localhost:3000`.
- Seeded demo logins are listed in the doc comment at the top of `Persistance/AppData/DataSeeder.cs`, e.g. `admin@clinicflow.com / Admin@1234`.

## Solution layout

Clean Architecture, one project per layer. Folders are listed per project, and each project's `Global.cs` already imports the namespaces shown in the last column. Check it before adding usings. In Persistence and Services.Abstraction the file is named `Global.cs.cs`.

| Project (folder) | Contents | `Global.cs` imports |
|---|---|---|
| `Domain` | `Entities/AppModule` (doctor, patient, schedule, slot, appointment, review, specialty), `Entities/IdentityModule` (`ApplicationUser`, `Notification`, `RefreshToken`), `Enums`, `Interfaces/IUnitOfWork.cs`, `Interfaces/IRepositories/I*Repo.cs`, `Parameters/*FilterParams.cs` | `Domain.Enums`, `Domain.Entities.AppModule`, `Domain.Parameters`, `Shared.Enums`, Identity |
| `Shared` | `DTOs/<Feature>/` request/response records, `Errors/<Feature>Errors.cs`, `ResultPattern/` (`Result`, `Result<T>`, `Error`, `ErrorType`), `Settings/` (`JwtSettings`, `EmailSettings`) | none |
| `Persistance` (project `Persistence.csproj`, namespaces `Persistence.*`) | `AppData/ClinicDbContext.cs`, `AppData/DataSeeder.cs`, `AppData/Configurations/<Entity>Config.cs`, `AppData/Migrations/`, `Repositories/<Name>Repo.cs`, `UnitOfWork.cs` | EF Core, `Persistence.AppData`, `Domain.Interfaces.IRepositories`, entities |
| `Services.Abstraction` | `Contracts/I<Name>Service.cs`, plus `IEmailService`, `ITokenService`, `IDateTimeProvider` | `Shared.DTOs`, `Shared.ResultPattern` |
| `Services` | `Implementations/<Name>Service.cs`, `MappingProfiles/<Feature>Profile.cs` (AutoMapper), `Extensions/CoreServiceExtensions.cs` (DI), `ServiceAssemblyReference.cs` (assembly marker) | AutoMapper, logging, `Services.Abstraction.Contracts`, `Shared.Errors`, `Shared.ResultPattern`, `Domain.Interfaces` |
| `Presentation` | `Controllers/<Name>Controller.cs` + `ApiController.cs` (base), `Validators/<Request>Validator.cs`, `Filters/ValidationFilter.cs`, `Extensions/HttpContextExtensions.cs` | MVC, `FluentValidation`, `Shared.ResultPattern`, `Shared.DTOs.Doctor`/`Patient` |
| `ClinicFlow` (host) | `Program.cs`, `Extensions/WebApiExtensions.cs` (MVC, filters, validators), `Extensions/InfraStructureServiceExtensions.cs` (DbContext, Identity, JWT, `IUnitOfWork`), `BackgroundJobs/AppointmentReminderJob.cs` (hourly `BackgroundService`) | Serilog, `ClinicFlow.Extensions`, `Services.Extensions` |
| `ClinicFlow.Tests` | `Services/<Service>Tests/` with a `*TestsBase` and one class per method | |
| `ClinicFlow.IntegrationTests` | `Infrastructure/` (`ClinicFlowApiFactory`, `TestAuthHandler`, `SeedDataHelper`, `IntegrationTestCollection`), `Tests/<Feature>/` | |

Project references: `Shared → Domain`, `Persistence → Domain`, `Services.Abstraction → Shared`, `Services → Domain, Persistence, Services.Abstraction`, `Presentation → Services.Abstraction`, `ClinicFlow → Persistence, Presentation, Services`.

`Program.cs` calls `AddWebApiServices` / `AddInfraStructureServices` / `AddCoreServices`, registers the reminder job and Serilog, and ends with `public partial class Program` so tests can use `WebApplicationFactory<Program>`.

### Namespace gotchas
Enum namespaces don't match their folder:
- `AppointmentStatus`, `NotificationType` and `DoctorSortBy` are in `Domain.Enums`.
- `SlotStatus`, `CancelledBy` and `BloodType` are in `ClinicFlow.Domain.Enums`.
- `Gender` is in `Shared.Enums`.

Files often need several of these usings. `IReviewRepo` is declared in namespace `Persistence.Repositories` even though it lives in Domain. `NotificationService` is in the global namespace.

## Conventions

### Naming
- **Repositories:** `I<Name>Repo` / `<Name>Repo`. **Services:** `I<Name>Service` / `<Name>Service`.
- **Persistence and mapping:** `<Entity>Config : IEntityTypeConfiguration<T>` for EF configs, `<Feature>Profile : Profile` for AutoMapper.
- **Errors and validation:** `<Feature>Errors` (static class), `<Request>Validator : AbstractValidator<TRequest>`.
- **DTOs:** records named `*Request` / `*Response`. List filters are `*FilterRequest` (DTO), mapped to `Domain/Parameters/*FilterParams`.
- **Async methods:** end in `Async` and take `CancellationToken ct` as the **last** parameter, passed down to every EF call.
- **Constructors:** services, repositories, controllers and jobs use primary constructors (`public class XService(IUnitOfWork uow, IMapper mapper, ILogger<XService> logger) : IXService`).
- **Logging:** structured Serilog templates with named placeholders (`"... {AppointmentId}"`), never string interpolation. Log a warning before returning an expected failure.

### Errors and results
- Services never throw for expected failures. They return `Result` / `Result<T>`. Return an `Error` or a value directly; implicit conversions do the wrapping. A `Result` can't mix `Validation` errors with other types; doing so throws.
- In `Shared/Errors/<Feature>Errors.cs`, declare fixed errors as `public static readonly Error X = Error.Conflict("Feature.Reason", "...")` and parameterized ones as `public static Error NotFound(int id) => Error.NotFound(...)`. Codes are `Feature.Reason`.
- Pick the `ErrorType` for the status code it produces: NotFound→404, Conflict→409, Forbidden→403, Validation→400, Unauthorized/InvalidCredentials→401, Failure→500. Use `Failure` only for unexpected server errors; some existing errors misuse it (see `docs/REVIEW.md` M3). Integration tests assert status codes, so changing a type changes the API.
- Notification and email failures after the main write are logged and swallowed. They never fail the main operation.
- Use the injected `IDateTimeProvider` for "now" in business rules (the past-slot check, the 2-hour cancellation window). Unit tests stub it.

### Endpoints
- Controllers live in `Presentation/Controllers`, inherit `ApiController` (not `ControllerBase`), take the service through a primary constructor, and set an explicit route: `[Route("api/appointments")]`. The base `api/[controller]` route is only used by `SpecialtyController`.
- Every action has:
  - `[EndpointSummary]`, `[EndpointDescription]`, and `[ProducesResponseType]` per status code (`ProblemDetails` / `ValidationProblemDetails` for errors)
  - `CancellationToken ct` as the last parameter
  - a body of `return HandleResult(await service.XAsync(..., ct));`
- `HandleResult(Result)` returns 204 by default. `HandleResult<T>(Result<T>)` returns 200, or 201 when given a location URI or a status code.
- Get the user id with `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` and the role from `ClaimTypes.Role`. Roles are `Admin`, `Doctor` and `Patient`.
- **Always add `[Authorize]` / `[Authorize(Roles = "...")]` explicitly.** There is no fallback authorization policy, so an action without one is anonymous.
- Request bodies are validated automatically by the global `ValidationFilter`. Add a `<Request>Validator` in `Presentation/Validators`; nothing else is needed.
- Enums are serialized as strings (`JsonStringEnumConverter`), and several are stored as strings in the DB.

### Registering things (new feature checklist)
1. **Entity:**
   - Put the entity in `Domain/Entities/AppModule` (or `IdentityModule`).
   - Add a `DbSet` property to `ClinicDbContext`.
   - Add `<Entity>Config` in `Persistance/AppData/Configurations`. It is picked up automatically by `ApplyConfigurationsFromAssembly`.
   - Add a migration.
2. **Repository:**
   - Put the interface in `Domain/Interfaces/IRepositories` and the implementation in `Persistance/Repositories` (primary constructor taking `ClinicDbContext context`).
   - Expose it as a property on `IUnitOfWork` and as a lazy `_x ??= new XRepo(_context)` property on `UnitOfWork`.
   - Repositories are **not** registered in DI. Services reach them only through `IUnitOfWork`.
3. **Service:**
   - Put the interface in `Services.Abstraction/Contracts` and the implementation in `Services/Implementations`.
   - Register it **manually** with `services.AddScoped<IXService, XService>()` in `Services/Extensions/CoreServiceExtensions.cs`.
   - Services inject `IUnitOfWork`, never `ClinicDbContext`. `AuthService`, `TokenService` and `ProfileService` also use Identity's `UserManager`.
4. **DTOs, mapping, validation:**
   - Put records in `Shared/DTOs/<Feature>/`.
   - Put the AutoMapper profile in `Services/MappingProfiles`. It is auto-registered by assembly scan of `ServiceAssemblyReference`.
   - Put the validator in `Presentation/Validators`. It is auto-registered via `AddValidatorsFromAssembly`.
5. **Errors:** add them to `Shared/Errors/<Feature>Errors.cs`.
6. **Controller:**
   - Add it to `Presentation/Controllers`. Controllers are discovered via `AddApplicationPart` in `WebApiExtensions.cs`.
7. **Infrastructure and background work:**
   - Infrastructure registrations (DbContext, Identity, auth, options) go in `ClinicFlow/Extensions/InfraStructureServiceExtensions.cs`. Options use `services.Configure<T>(config.GetSection(...))` / `AddOptions<T>().Bind(...)` with POCOs in `Shared/Settings`.
   - Background jobs are `BackgroundService` classes in `ClinicFlow/BackgroundJobs`, registered in `Program.cs` with `AddHostedService`. Each one creates a scope per run via `IServiceScopeFactory`.
   - Remove new jobs in `ClinicFlowApiFactory` so they don't run during integration tests.

### Data access
- **Double-booking guard:** `SlotRepo.SetStatusFromAvailableToBookedAsync` runs a conditional `ExecuteUpdate` (`WHERE Status = Available`) inside a transaction. If it affects 0 rows, the slot was already booked. `AppointmentSlot.RowVersion` is not what prevents double-booking.
- **Reads:** use `AsNoTracking()` on read-only queries.
- **Bulk writes:** use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (these need no `SaveChangesAsync`).
- **Transactions:** `uow.BeginTransactionAsync()` + `await using`.

## Tests

- **Unit (`ClinicFlow.Tests`):** xUnit + NSubstitute. Each service gets a folder with a `*TestsBase` that substitutes every constructor dependency and builds `_sut` (see `Services/AppointmentServiceTests/AppointmentServiceTestsBase.cs`), plus one test class per method (`BookAppointmentAsyncTests`, ...).
- **Integration (`ClinicFlow.IntegrationTests`):** xUnit + FluentAssertions + `Microsoft.AspNetCore.Mvc.Testing`.
  - **Factory:** `ClinicFlowApiFactory` starts an MsSql Testcontainer, points `ClinicDbContext` at it, removes `AppointmentReminderJob`, and replaces JWT auth with `TestAuthHandler`.
  - **Authentication:** set the headers `TestAuthHandler.UserIdHeader` (`X-Test-UserId`) and `UserRoleHeader` (`X-Test-Role`) on a request to authenticate it. Without them the request is anonymous.
  - **Test classes:** every test class has `[Collection(nameof(IntegrationTestCollection))]`, so they share one container and run sequentially. Each one implements `IAsyncLifetime`. Its `InitializeAsync` calls `_factory.ResetDataBaseAsync()`, which drops and re-migrates the DB **without** the demo seed. It then clears the client headers and seeds its own data with `_factory.SeedAsync(db => SeedDataHelper...)`. Pass a distinct `suffix` to seed more than one set of entities.
- **Naming:** test names follow `Method_Scenario_ExpectedResult`.

Known issues and their severities are tracked in `docs/REVIEW.md`. Check it before "fixing" something that looks wrong; it may be a recorded decision or have a noted fix risk.
