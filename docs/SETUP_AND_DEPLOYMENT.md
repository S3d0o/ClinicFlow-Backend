# ClinicFlow Backend: Setup and Deployment

How to run the API locally, configure it, test it, and deploy it. For what the system does, see [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md). For the API itself, see [API_CONTRACT.md](API_CONTRACT.md).

> **V2 (`v2` branch).** V2 is a separate product line with its own database, `ClinicFlowV2` ([ADR 0007](v2/adr/0007-v2-separate-product-line.md)). This branch has its own user-secrets id, and the app **refuses to start against any other database**, including V1's `ClinicFlow`. That check also covers `dotnet ef`. Its compose stack uses its own project name, volume and ports. The git-ignored `appsettings.Development.json` is shared with V1 (same working tree) and normally names V1's database, so set V2's connection string in V2's user secrets, which take precedence.

---

## 1. Prerequisites

| Tool | Needed for |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build, run, test |
| SQL Server 2022 (local install, or the Docker command below) | Running the API |
| Docker Desktop | Integration tests (Testcontainers), and running SQL Server or the whole stack in containers |
| `dotnet-ef` (`dotnet tool install --global dotnet-ef`) | Creating or applying migrations by hand |
| Seq (optional, `http://localhost:5341`) | Viewing structured logs |

## 2. Configuration

`appsettings.json` contains only non-secret defaults. Provide everything else through **user secrets** (recommended locally), the git-ignored `ClinicFlow/appsettings.Development.json`, or **environment variables** (`__` replaces `:`, e.g. `JwtSettings__Secret`).

| Key | Required | Default / notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | **Yes** | The app refuses to start without it, or when it names a database other than `Database:ExpectedName` |
| `Database:ExpectedName` | No | `ClinicFlowV2` in `appsettings.json`: the only database V2 starts against |
| `JwtSettings:Secret` | **Yes** | At least 32 bytes. Checked at startup |
| `JwtSettings:Issuer`, `JwtSettings:Audience` | Yes | Set in `appsettings.json` (`https://localhost:7129` / `https://localhost:3000`) |
| `JwtSettings:AccessTokenExpirationMinutes` / `RefreshTokenExpirationDays` / `TheftDetectionWindowMinutes` | Yes | 15 / 7 / 5 in `appsettings.json` |
| `EmailSettings:Username`, `EmailSettings:Password`, `EmailSettings:FromAddress` | For email | SMTP credentials (Gmail: use an app password). Host `smtp.gmail.com`, port 587 in `appsettings.json`. Without them, startup logs a warning naming the missing keys, and every send fails and is logged |
| `Frontend:BaseUrl` | No | `http://localhost:3000`. CORS origin and base of the email links |
| `RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds` | No | 10 / 60 in `appsettings.json`: requests per client IP per window, shared by login, both registrations, confirm-email, forgot-password and reset-password. Over the limit: 429 `RateLimit.Exceeded` |
| `Seeding:DemoData` | No | `true` in Development, `false` elsewhere. Creates the demo clinic with **published passwords** |
| `Seeding:AdminEmail`, `Seeding:AdminPassword` | No | When both are set, an admin with these credentials is created at startup if one doesn't exist |

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=127.0.0.1,1433;Database=ClinicFlowV2;User Id=sa;Password=<pwd>;TrustServerCertificate=True" --project ClinicFlow
dotnet user-secrets set "JwtSettings:Secret" "<random string of at least 32 characters>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Username" "<smtp user>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:Password" "<smtp app password>" --project ClinicFlow
dotnet user-secrets set "EmailSettings:FromAddress" "<from address>" --project ClinicFlow
```

To generate a secret, run PowerShell: `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }) -as [byte[]])`

## 3. Running locally

```bash
# 1. SQL Server in Docker (skip if you have a local instance)
docker run -d --name clinicflow-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<pwd>' -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest

# 2. Configure secrets (section 2), then run
dotnet run --project ClinicFlow/ClinicFlow.csproj                          # http://localhost:5143
dotnet run --project ClinicFlow/ClinicFlow.csproj --launch-profile https   # https://localhost:7129
```

**On startup the app:**
1. applies pending migrations in Development (`Database:MigrateOnStartup`, default `true` in Development only). Elsewhere it only checks that none are pending and refuses to start if any are: the migration bundle applies them first (section 6)
2. seeds roles and specialties
3. creates the bootstrap admin, if configured
4. in Development, seeds the demo clinic

**Demo logins** (Development only; full list in `Persistance/AppData/DataSeeder.cs`):

| Role | Email | Password |
|---|---|---|
| Admin | `admin@clinicflow.com` | `Admin@1234` |
| Doctor | `ahmed.elsayed@clinicflow.com` | `Doctor@1234` |
| Patient | `mohamed.ali@gmail.com` | `Patient@1234` |
| Generated doctors | `doctor01@clinicflow.test` … `doctor53@clinicflow.test` | `Doctor@1234` |
| Generated patients | `patient01@clinicflow.test` … `patient75@clinicflow.test` | `Patient@1234` |

The demo clinic has 60 doctors, 80 patients and about 2,450 appointments over 60 days of history and 21 ahead (details in [V1_GUIDE.md §12](V1_GUIDE.md#12-running-demo-data-and-tests)). The first start on an empty database takes about 30 seconds longer while it is seeded.

**API docs:** Scalar at `/scalar` and the OpenAPI document at `/openapi/v1.json` (Development only).

## 4. Database migrations

```bash
# add (PascalCase name, no spaces)
dotnet ef migrations add <Name> --project Persistance/Persistence.csproj --startup-project ClinicFlow --output-dir AppData/Migrations
# apply without starting the app
dotnet ef database update --project Persistance/Persistence.csproj --startup-project ClinicFlow
# remove the last, unapplied migration
dotnet ef migrations remove --project Persistance/Persistence.csproj --startup-project ClinicFlow
```

`dotnet ef` builds the host, so `ConnectionStrings:DefaultConnection` must be configured, and it must name `ClinicFlowV2`. In Development, startup also applies migrations automatically.

**Deployments** apply migrations with a migration bundle, not at startup (docs/v2/plans/P0.md D1). The Docker image carries it as `/app/efbundle`; it reads `ConnectionStrings__DefaultConnection` like the app, so run it with a login that may change the schema (compose's `migrate` service does this, section 6). To build one by hand: `dotnet ef migrations bundle --project Persistance/Persistence.csproj --startup-project ClinicFlow --output efbundle`.

## 5. Tests

```bash
dotnet test ClinicFlow.Tests               # unit tests, no dependencies
dotnet test ClinicFlow.IntegrationTests    # needs Docker running (starts SQL Server via Testcontainers)
dotnet test ClinicFlow.Tests --filter "FullyQualifiedName~BookAppointmentAsyncTests"
```

Integration tests need no local configuration. `ClinicFlowApiFactory` sets the connection string (the Testcontainers database `ClinicFlowV2TestDb`), the expected database name and a test JWT secret itself.

## 6. Docker

### Image

```bash
docker build -t clinicflow-backend-v2 .   # Dockerfile is at the repo root
```

### Compose (db + backend + frontend)

1. Build `clinicflow-backend-v2` (above) and `clinicflow-frontend-v2` (from the frontend repo's `v2` branch).
2. Copy [`../.env.example`](../.env.example) to `.env` and fill it in. V1's and V2's stacks read the same file.
3. `docker compose -p clinicflow-v2 up -d`. Pass `-p clinicflow-v2` every time (`up`, `down`, `logs`): a `COMPOSE_PROJECT_NAME` in the shared `.env`, such as V1's `clinicflow`, overrides the compose file's `name:` and would make V2 adopt V1's containers. The compose project is `clinicflow-v2`, with its own containers and the volume `clinicflow_v2_data`, so V1's stack can run at the same time.

| Service | URL (V1's stack uses 3000 / 5001 / 1433) |
|---|---|
| Frontend | http://localhost:3001 |
| Backend | http://localhost:5002 |
| SQL Server | `127.0.0.1:1434` (host only) |

Compose runs with `ASPNETCORE_ENVIRONMENT=Production`, so **no demo data** by default. Set `ADMIN_EMAIL` / `ADMIN_PASSWORD` to get a first admin. For a local demo, set `SEED_DEMO_DATA=true` in `.env` to load the demo clinic (published passwords: never on a real deployment).

**Start order.** `db` → `db-init` (database and logins) → `migrate` (applies pending migrations, then exits) → `backend`. A failed migration stops the backend from starting; `docker compose logs migrate` shows why.

**Database logins.** Only `db` and `db-init` use `sa`. The one-shot `db-init` service runs [`docker/db-init.sql`](../docker/db-init.sql) as `sa`; it creates the `ClinicFlowV2` database and two logins (names from `APP_LOGIN` / `APP_DEPLOY_LOGIN` in compose):
- `clinicflow_v2_deploy` (`DEPLOY_DB_PASSWORD`) is `db_owner` of `ClinicFlowV2` only. The `migrate` service uses it to run the migration bundle.
- `clinicflow_v2_app` (`APP_DB_PASSWORD`) is what the backend connects as. It is a member of the `clinicflow_app` role, which the `AddAppDatabaseRole` migration sets up: read and write data, no schema changes, and no `UPDATE` or `DELETE` on `AuditEntries` (append-only audit, FR-AUD-04).

The script is idempotent and re-applies both passwords, so a changed password takes effect on the next `docker compose up`. **Upgrading:** add `DEPLOY_DB_PASSWORD` to `.env` (compose refuses to start without it). On an existing volume, `db-init` moves `clinicflow_v2_app` out of `db_owner` into the role.

## 7. Production checklist

- [ ] Secrets come from the environment or a secret store, never from `appsettings.json`. `JwtSettings:Secret` is unique per environment and at least 32 bytes.
- [ ] `Seeding:DemoData` is not `true`. If the database was ever seeded with demo data, delete those accounts or change their passwords.
- [ ] A bootstrap admin is configured (`Seeding:AdminEmail` / `Seeding:AdminPassword`). Remove the password from the environment once the admin exists, and change it.
- [ ] `Frontend:BaseUrl` is the real frontend origin (CORS and email links).
- [ ] `JwtSettings:Issuer` / `Audience` match the deployed URLs.
- [ ] Email settings are complete (`Username`, `Password`, `FromAddress`). If any is missing, startup logs the warning "Email is not configured" and lists the missing keys.
- [ ] `RateLimiting:Auth` suits the deployment. The default is 10 requests per minute per client IP, shared across login, registration, confirm-email and the password-reset endpoints. Clients behind one NAT address share that budget.
- [ ] Behind a reverse proxy, the proxy is added to `ForwardedHeadersOptions.KnownProxies` (`ClinicFlow/Extensions/WebApiExtensions.cs`). Otherwise client IPs show as the proxy's address.
- [ ] HTTPS terminates at the proxy or at Kestrel.
- [ ] The app connects with a read/write login in the `clinicflow_app` role, never `sa` or `db_owner`, and migrations run as a separate deploy login before the app starts. Compose does this with `db-init` and `migrate`; elsewhere, run [`docker/db-init.sql`](../docker/db-init.sql) and the migration bundle the same way (REVIEW NM6). `Database:MigrateOnStartup` is not `true`.
- [ ] Only one instance runs the reminder job (there is no distributed lock yet; REVIEW M9).
- [ ] Logs: the Seq URL is configured, or the Seq sink is removed; `logs/` is on persistent storage, if file logs are wanted.
- [ ] The orchestrator restarts the container on failure (compose: `restart: unless-stopped`) and probes `GET /health/live` (the process answers; `/health` is the same). A load balancer routes by `GET /health/ready`: 503 when the database can't be reached, and 200 with `"status": "Degraded"` when the oldest due outbox message has waited over a minute (notifications and emails are stuck; the API still serves). Startup failures exit with 1.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Connection string 'DefaultConnection' not found` at startup | Set the connection string (section 2) |
| `... points at database 'ClinicFlow', but V2 must use 'ClinicFlowV2'` at startup or from `dotnet ef` | The connection string comes from the shared `appsettings.Development.json`, which names V1's database. Set V2's own connection string with `Database=ClinicFlowV2` in V2's user secrets (section 2). Don't change `Database:ExpectedName` |
| `OptionsValidationException: JwtSettings:Secret must be at least 32 bytes` | Set a longer secret |
| `A network-related or instance-specific error … wait operation timed out` with `Server=localhost,<port>` | `localhost` may resolve to IPv6 while SQL Server is bound to IPv4. Use `127.0.0.1` |
| Integration tests fail to start | Docker isn't running |
| Log shows `Database not ready (SQL error 1801 …). Retrying in 5s` | Normal when SQL Server and the API start together: SQL Server is still recovering the database. Startup retries for about a minute; if it still fails, check that SQL Server is up |
| `ports are not available: … 127.0.0.1:1434` on `docker compose up` | Something on the host already listens on 1434, such as a local SQL Server instance. Stop it, or map the db service to another host port with an override file (`ports: !override ["127.0.0.1:<port>:1433"]`, then `-f docker-compose.yml -f <override>`) |
| `The database is missing N migration(s): …` at startup | Migrations don't run at startup outside Development. Run the migration bundle first (compose: the `migrate` service; check `docker compose logs migrate`), or set `Database:MigrateOnStartup=true` on a development machine |
| Demo appointments are all in the past | The demo dataset is created once and keeps its dates (slots are topped up on every start). Drop the database and restart to reseed. A database seeded before the large demo clinic existed gets the generated doctors, patients and their appointments on its next start; no drop needed |
| Login returns `Auth.AccountLockedOut` | 5 wrong passwords: wait 15 minutes, or clear `LockoutEnd` for the user |
| Login returns `Auth.EmailNotConfirmed` | Confirm the email, or set `EmailConfirmed = 1` in `Users` for local testing |
| No slots for a doctor | The doctor has no active schedule for that weekday, is not approved or is suspended, or the date is more than 14 days ahead. The daily `SlotGenerationJob` extends the window at startup and then every 24 hours |
| Emails not arriving | Check the logs for `Failed to send …`. Verify the SMTP credentials and `FromAddress` |

## Regenerating openapi.json

`docs/openapi.json` is generated from the running API. After any endpoint or DTO change:

1. Run the API in Development (section 3).
2. Save the document: `curl http://localhost:5143/openapi/v1.json -o docs/openapi.json`
3. Set `servers[0].url` to `http://localhost:5143/`.
4. Commit it together with the [API_CONTRACT.md](API_CONTRACT.md) update.
