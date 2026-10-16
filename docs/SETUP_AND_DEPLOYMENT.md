# ClinicFlow Backend: Setup and Deployment

How to run the API locally, configure it, test it, and deploy it. For what the system does, see [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md). For the API itself, see [API_CONTRACT.md](API_CONTRACT.md).

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
| `ConnectionStrings:DefaultConnection` | **Yes** | The app refuses to start without it |
| `JwtSettings:Secret` | **Yes** | At least 32 bytes. Checked at startup |
| `JwtSettings:Issuer`, `JwtSettings:Audience` | Yes | Set in `appsettings.json` (`https://localhost:7129` / `https://localhost:3000`) |
| `JwtSettings:AccessTokenExpirationMinutes` / `RefreshTokenExpirationDays` / `TheftDetectionWindowMinutes` | Yes | 15 / 7 / 5 in `appsettings.json` |
| `EmailSettings:Username`, `EmailSettings:Password`, `EmailSettings:FromAddress` | For email | SMTP credentials (Gmail: use an app password). Host `smtp.gmail.com`, port 587 in `appsettings.json`. Without them, emails fail and the failures are logged |
| `Frontend:BaseUrl` | No | `http://localhost:3000`. CORS origin and base of the email links |
| `Seeding:DemoData` | No | `true` in Development, `false` elsewhere. Creates the demo clinic with **published passwords** |
| `Seeding:AdminEmail`, `Seeding:AdminPassword` | No | When both are set, an admin with these credentials is created at startup if one doesn't exist |

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=127.0.0.1,1433;Database=ClinicFlow;User Id=sa;Password=<pwd>;TrustServerCertificate=True" --project ClinicFlow
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
1. applies pending migrations
2. seeds roles and specialties
3. creates the bootstrap admin, if configured
4. in Development, seeds the demo clinic

**Demo logins** (Development only; full list in `Persistance/AppData/DataSeeder.cs`):

| Role | Email | Password |
|---|---|---|
| Admin | `admin@clinicflow.com` | `Admin@1234` |
| Doctor | `ahmed.elsayed@clinicflow.com` | `Doctor@1234` |
| Patient | `mohamed.ali@gmail.com` | `Patient@1234` |

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

`dotnet ef` builds the host, so `ConnectionStrings:DefaultConnection` must be configured. Startup also applies migrations automatically.

## 5. Tests

```bash
dotnet test ClinicFlow.Tests               # unit tests, no dependencies
dotnet test ClinicFlow.IntegrationTests    # needs Docker running (starts SQL Server via Testcontainers)
dotnet test ClinicFlow.Tests --filter "FullyQualifiedName~BookAppointmentAsyncTests"
```

Integration tests also need `ConnectionStrings:DefaultConnection` and `JwtSettings:Secret` in the local configuration. The connection string's value is irrelevant, because the tests replace the database.

## 6. Docker

### Image

```bash
docker build -t clinicflow-backend .      # Dockerfile is at the repo root
```

### Compose (db + backend + frontend)

1. Build `clinicflow-backend` (above) and `clinicflow-frontend` (from the frontend repo).
2. Copy [`../.env.example`](../.env.example) to `.env` and fill it in.
3. `docker compose up -d`

| Service | URL |
|---|---|
| Frontend | http://localhost:3000 |
| Backend | http://localhost:5001 |
| SQL Server | `127.0.0.1:1433` (host only) |

Compose runs with `ASPNETCORE_ENVIRONMENT=Production`, so **no demo data**. Set `ADMIN_EMAIL` / `ADMIN_PASSWORD` to get a first admin.

⚠️ `docker-compose.yml` does not set `EmailSettings__FromAddress` yet. Add `EmailSettings__FromAddress: ${EMAIL_FROM_ADDRESS}` under `backend.environment` (and the variable to `.env`), or emails will fail.

## 7. Production checklist

- [ ] Secrets come from the environment or a secret store, never from `appsettings.json`. `JwtSettings:Secret` is unique per environment and at least 32 bytes.
- [ ] `Seeding:DemoData` is not `true`. If the database was ever seeded with demo data, delete those accounts or change their passwords.
- [ ] A bootstrap admin is configured (`Seeding:AdminEmail` / `Seeding:AdminPassword`). Remove the password from the environment once the admin exists, and change it.
- [ ] `Frontend:BaseUrl` is the real frontend origin (CORS and email links).
- [ ] `JwtSettings:Issuer` / `Audience` match the deployed URLs.
- [ ] Email settings are complete (`Username`, `Password`, `FromAddress`).
- [ ] Behind a reverse proxy, the proxy is added to `ForwardedHeadersOptions.KnownProxies` (`ClinicFlow/Extensions/WebApiExtensions.cs`). Otherwise client IPs show as the proxy's address.
- [ ] HTTPS terminates at the proxy or at Kestrel.
- [ ] The database login is a dedicated least-privilege user, not `sa` (see REVIEW NM6).
- [ ] Only one instance runs the reminder job (there is no distributed lock yet; REVIEW M9).
- [ ] Logs: the Seq URL is configured, or the Seq sink is removed; `logs/` is on persistent storage, if file logs are wanted.
- [ ] The orchestrator restarts the container on failure (compose: `restart: unless-stopped`), and probes `GET /health` (liveness only; it doesn't check the database). Startup failures exit with 1.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Connection string 'DefaultConnection' not found` at startup | Set the connection string (section 2) |
| `OptionsValidationException: JwtSettings:Secret must be at least 32 bytes` | Set a longer secret |
| `A network-related or instance-specific error … wait operation timed out` with `Server=localhost,<port>` | `localhost` may resolve to IPv6 while SQL Server is bound to IPv4. Use `127.0.0.1` |
| Integration tests fail to start | Docker isn't running |
| Log shows `Database not ready (SQL error 1801 …). Retrying migration` | Normal when SQL Server and the API start together: SQL Server is still recovering the database. Startup retries for about a minute; if it still fails, check that SQL Server is up |
| Demo appointments are all in the past | The demo dataset is created once and keeps its dates (slots are topped up on every start). Drop the database and restart to reseed |
| Login returns `Auth.AccountLockedOut` | 5 wrong passwords: wait 15 minutes, or clear `LockoutEnd` for the user |
| Login returns `Auth.EmailNotConfirmed` | Confirm the email, or set `EmailConfirmed = 1` in `Users` for local testing |
| No slots for a doctor | The doctor has no active schedule for that weekday, or the date is more than 14 days ahead |
| Emails not arriving | Check the logs for `Failed to send …`. Verify the SMTP credentials and `FromAddress` |

## Regenerating openapi.json

`docs/openapi.json` is generated from the running API. After any endpoint or DTO change:

1. Run the API in Development (section 3).
2. Save the document: `curl http://localhost:5143/openapi/v1.json -o docs/openapi.json`
3. Set `servers[0].url` to `http://localhost:5143/`.
4. Commit it together with the [API_CONTRACT.md](API_CONTRACT.md) update.
