# 🏥 ClinicFlow

> A production-ready clinic management REST API built with **.NET 10**, following **Clean Architecture** principles.

ClinicFlow handles the full lifecycle of a clinic: patient and doctor registration, admin-gated doctor approval, schedule and slot management, appointment booking with race-condition protection, email notifications, appointment reminders, and a review system — all backed by JWT authentication with refresh-token rotation.

---

## ✨ Features

- **Role-based access** — three distinct roles: `Patient`, `Doctor`, `Admin`, each with their own set of endpoints
- **Doctor onboarding flow** — doctors register, wait for admin approval, then publish their schedules; unapproved doctors are invisible in search
- **Appointment booking** — patients browse available slots, book them, and cancel; doctors confirm or add notes; optimistic concurrency (`RowVersion`) prevents double-bookings under load
- **Automated reminders** — a hosted `BackgroundService` runs every hour and sends email + in-app notifications for appointments scheduled the next day
- **Refresh token rotation** — short-lived access tokens (15 min) paired with rotating refresh tokens (7 days) and theft detection
- **Email integration** — confirmation emails, password reset, appointment reminders, and booking confirmations via SMTP (MailKit)
- **Result pattern** — all service methods return a typed `Result<T>` instead of throwing exceptions, giving consistent error propagation to the API layer
- **Structured logging** — Serilog with Console, rolling File, and Seq sinks; every request enriched with TraceId, path, method, and client IP
- **OpenAPI + Scalar** — interactive API docs available in development at `/scalar`
- **Docker support** — multi-stage `Dockerfile` included

---

## 🏛️ Architecture

The solution follows **Clean Architecture** with a clear separation of concerns across six projects:

```
ClinicFlow.sln
├── Domain                  # Entities, interfaces, enums, domain parameters
├── Shared                  # DTOs, error types, Result pattern, settings
├── Persistance             # EF Core DbContext, entity configurations, migrations, repositories, UoW
├── Services.Abstraction    # Service contracts (interfaces only)
├── Services                # Business logic, AutoMapper profiles, email service
├── Presentation            # Controllers, FluentValidation, API filters
└── ClinicFlow              # Entry point — DI wiring, middleware, background jobs
```

**Dependency flow:**
```
ClinicFlow → Presentation → Services.Abstraction
                          → Services → Persistance → Domain
                                    ↘ Shared ↗
```

---

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core 10 |
| ORM | Entity Framework Core 10 |
| Database | SQL Server |
| Authentication | ASP.NET Core Identity + JWT Bearer |
| Mapping | AutoMapper 16 |
| Validation | FluentValidation 12 |
| Email | MailKit 4 |
| Logging | Serilog (Console · File · Seq) |
| API docs | Scalar + ASP.NET Core OpenAPI |
| Containerization | Docker (multi-stage, Windows Nano Server) |

---

## 📁 Domain Model

```
ApplicationUser (Identity)
├── DoctorProfile           ← specialty, fee, rating, clinic info, admin approval flag
│   ├── DoctorSchedule      ← recurring weekly template (day + time range)
│   ├── AppointmentSlot     ← materialized slots with RowVersion for concurrency
│   ├── Appointment         ← booking record; links Patient ↔ Slot ↔ Doctor
│   └── Review              ← one per completed appointment; updates doctor avg rating
└── PatientProfile          ← blood type, medical history, emergency contact

Notification                ← per-user inbox (appointment booked / reminder / cancelled)
RefreshToken                ← hashed, IP-tracked, rotation-aware
```

**Enums:** `AppointmentStatus`, `SlotStatus`, `CancelledBy`, `NotificationType`, `BloodType`, `Gender`, `DoctorSortBy`

---

## 🔌 API Overview

| Prefix | Who | What |
|---|---|---|
| `POST /api/auth/*` | Public | Register patient/doctor, login, refresh token, email confirm, forgot/reset password |
| `GET /api/doctors` | Public | Paginated doctor search (filter by specialty, city; sort by rating/fee/experience) |
| `GET /api/doctors/{id}/slots` | Public | Available slots for a doctor on a given date |
| `GET /api/specialties` | Public | List medical specialties |
| `POST /api/appointments` | Patient | Book an available slot |
| `DELETE /api/appointments/{id}` | Patient / Doctor | Cancel an appointment |
| `PATCH /api/appointments/{id}/notes` | Doctor | Add clinical notes to a completed appointment |
| `GET /api/appointments` | Patient / Doctor | View own appointments with filters |
| `POST /api/doctors/schedule` | Doctor | Create weekly schedule (generates slots automatically) |
| `PUT /api/doctors/schedule/{id}` | Doctor | Update schedule |
| `POST /api/reviews` | Patient | Submit a review for a completed appointment |
| `GET /api/reviews` | Public | List reviews for a doctor |
| `GET /api/notifications/my` | Authenticated | Fetch notifications (optionally filter unread) |
| `PATCH /api/notifications/{id}/read` | Authenticated | Mark a notification as read |
| `GET /api/profile/me` | Authenticated | Get own user profile |
| `GET /api/admin/doctors/pending` | Admin | List doctors awaiting approval |
| `POST /api/admin/doctors/{id}/approve` | Admin | Approve a doctor |
| `GET /api/admin/overview` | Admin | Platform stats (total users, appointments, revenue) |

Full interactive docs are available at `/scalar` when running in Development mode.

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local or remote)
- (Optional) [Seq](https://datalust.co/seq) for structured log viewing

### 1. Clone the repository

```bash
git clone https://github.com/<your-username>/ClinicFlow.git
cd ClinicFlow
```

### 2. Configure settings

Open `ClinicFlow/appsettings.json` and update the following sections:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=ClinicFlow;..."
  },
  "JwtSettings": {
    "Secret": "<your-256-bit-secret>",
    "Issuer": "https://localhost:5143",
    "Audience": "ClinicFlowClient",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "EmailSettings": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "<your-email>",
    "Password": "<your-app-password>",
    "FromName": "ClinicFlow",
    "FromAddress": "<your-email>"
  }
}
```

> **Security:** Never commit real secrets. Use [user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) or environment variables in production.

```bash
dotnet user-secrets set "JwtSettings:Secret" "<your-secret>" --project ClinicFlow/ClinicFlow.csproj
```

### 3. Apply migrations and seed data

```bash
dotnet ef database update --project Persistance --startup-project ClinicFlow
```

The app also runs `DataSeeder.SeedAsync` on startup to populate roles (`Admin`, `Doctor`, `Patient`) and a default admin account.

### 4. Run the API

```bash
dotnet run --project ClinicFlow/ClinicFlow.csproj
```

Navigate to `https://localhost:5143/scalar` to explore the API interactively.

---

## 🐳 Docker

```bash
# Build the image from the solution root
docker build -f ClinicFlow/Dockerfile -t clinicflow .

# Run (pass your connection string as an env var)
docker run -p 8080:8080 \
  -e "ConnectionStrings__DefaultConnection=Server=host.docker.internal;..." \
  clinicflow
```

---

## 🔐 Authentication Flow

```
POST /api/auth/register/patient   → confirm email link sent
POST /api/auth/confirm-email      → account activated
POST /api/auth/login              → { accessToken (15 min), refreshToken (7 days) }
POST /api/auth/refresh-token      → issues new token pair; old refresh token invalidated
Authorization: Bearer <accessToken>
```

Refresh tokens are hashed before storage. A `TheftDetectionWindow` (5 min default) invalidates the entire token family if a previously used token is replayed.

---

## ⚙️ Background Jobs

**`AppointmentReminderJob`** — runs every hour via `PeriodicTimer`:
1. Queries appointments scheduled for tomorrow that haven't received a reminder yet
2. Creates an in-app `Notification` for the patient
3. Sends an email reminder via MailKit
4. Stamps `ReminderSentAt` on the appointment to prevent duplicate sends

---

## 📐 Design Patterns

| Pattern | Where |
|---|---|
| Clean Architecture | Solution structure (Domain → Persistance → Services → Presentation) |
| Repository + Unit of Work | `IUnitOfWork` aggregates all repos; single `SaveChangesAsync` call per request |
| Result pattern | `Result<T>` / `Result` used across all service returns — no exception-driven control flow |
| CQRS-lite | Services split into focused methods per use case; no shared mutable state |
| Optimistic concurrency | `RowVersion` on `AppointmentSlot` prevents double-booking races |
| Background Service | `IHostedService` for the appointment reminder job |
| Options pattern | `JwtSettings`, `EmailSettings` bound via `IOptions<T>` |

---

## 📂 Project Structure

```
ClinicFlow-master/
├── ClinicFlow/                         # Entry point
│   ├── BackgroundJobs/
│   │   └── AppointmentReminderJob.cs
│   ├── Extensions/
│   │   ├── InfraStructureServiceExtensions.cs
│   │   └── WebApiExtensions.cs
│   ├── Program.cs
│   └── Dockerfile
├── Domain/
│   ├── Entities/
│   │   ├── AppModule/                  # Appointment, Slot, Doctor/Patient profile, Review, Specialty
│   │   └── IdentityModule/             # ApplicationUser, RefreshToken, Notification
│   ├── Enums/
│   ├── Interfaces/
│   │   ├── IRepositories/
│   │   └── IUnitOfWork.cs
│   └── Parameters/                     # Filter params for queries
├── Persistance/
│   ├── AppData/
│   │   ├── Configurations/             # Fluent API entity configs
│   │   ├── Migrations/
│   │   ├── ClinicDbContext.cs
│   │   └── DataSeeder.cs
│   └── Repositories/
├── Services.Abstraction/
│   └── Contracts/                      # IAuthService, IAppointmentService, IDoctorService, ...
├── Services/
│   ├── Implementations/                # AuthService, AppointmentService, TokenService, EmailService, ...
│   └── MappingProfiles/                # AutoMapper profiles
├── Presentation/
│   └── Controllers/                    # Auth, Appointment, Doctor, Patient, Admin, Review, ...
└── Shared/
    ├── DTOs/                           # Request / Response models per domain
    ├── Errors/                         # Typed error constants per domain
    ├── ResultPattern/                  # Result<T>, Error, ErrorType
    └── Settings/                       # JwtSettings, EmailSettings
```

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Commit your changes: `git commit -m 'feat: add your feature'`
4. Push to your branch: `git push origin feature/your-feature`
5. Open a Pull Request

---

## 👤 Author

**Saad Mohamed**
- GitHub: [@S3d0o](https://github.com/S3d0o)
- LinkedIn: [linkedin.com/in/saad-mohamed-li](https://linkedin.com/in/saad-mohamed-li)

---

## 📄 License

This project is open source and available under the [MIT License](LICENSE).
