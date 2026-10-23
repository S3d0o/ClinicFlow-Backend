# ClinicFlow — Project Overview

ClinicFlow is a clinic-booking platform. Patients find doctors and book appointment slots. Doctors publish weekly schedules and manage their appointments. Admins approve doctors and manage the specialty catalogue.

This repository is the **backend**: a .NET 10 REST API. A separate React app is the frontend. The two apps agree on the API through [`API_CONTRACT.md`](API_CONTRACT.md).

| Document | Purpose |
|---|---|
| [V1_GUIDE.md](V1_GUIDE.md) | **The complete V1 guide:** every flow end to end, with diagrams; start here |
| **PROJECT_OVERVIEW.md** (this file) | What the system does, its rules, and how it is built |
| [API_CONTRACT.md](API_CONTRACT.md) | The shared backend ↔ frontend contract: endpoints, payloads, errors, auth |
| [openapi.json](openapi.json) | Machine-readable OpenAPI 3.1 document generated from the running API |
| [SETUP_AND_DEPLOYMENT.md](SETUP_AND_DEPLOYMENT.md) | Running locally, configuration, Docker, production checklist |
| [REVIEW.md](REVIEW.md) | Code review: known issues, their status and fix risk |
| [v2/README.md](v2/README.md) | V2 plan (draft): clinic operations, roles and permissions, EMR, billing, insurance, roadmap |
| [../CLAUDE.md](../CLAUDE.md) | Conventions and commands for working in this codebase |

---

## 1. Users and roles

Every account has exactly one role.

| Role | How the account is created | What they can do |
|---|---|---|
| **Patient** | Self-registration (`POST /api/auth/register/patient`), then email confirmation | Search doctors, view slots, book and cancel appointments, review completed visits, manage their profile and medical info, read notifications |
| **Doctor** | Self-registration (`POST /api/auth/register/doctor`), then email confirmation, then **admin approval** to appear in search | Manage their weekly schedule, see their appointments, cancel or complete them, add clinical notes, update their profile |
| **Admin** | Bootstrap admin from configuration (`Seeding:AdminEmail` / `Seeding:AdminPassword`), or the demo admin in Development | Approve or suspend doctors, manage specialties, cancel any appointment, delete reviews, view platform statistics |

## 2. Main features

**Discovery:**
- Public doctor search, filtered by specialty, city and name, sorted by rating, fee or experience, and paged.
- A doctor's profile, reviews and available slots for a given date.

**Scheduling:**
- A doctor defines one schedule per weekday: start time, end time and slot length.
- Bookable slots are generated from it automatically for the next 14 days.

**Booking:**
- A patient books an available slot.
- Booking is protected against double-booking at the database level.
- Both parties get an in-app notification, and the patient gets a confirmation email.

**Cancellation:**
- A patient can cancel up to 2 hours before the start time.
- The doctor and admins can cancel at any time.
- The slot becomes bookable again, unless the doctor's schedule no longer covers it (then it is blocked).

**Completion and reviews:**
- The doctor marks a visit as completed and can add notes.
- The patient can then leave one review (1–5 stars, optional comment).
- The doctor's average rating is recalculated.

**Reminders:**
- An hourly background job emails and notifies patients whose confirmed appointment starts within the next 24 hours.

**Notifications:**
- A per-user in-app inbox with read/unread state.

**Accounts:**
- Email confirmation, password reset by email, profile management.
- JWT access tokens with rotating refresh tokens, and login lockout.

**Admin:**
- Doctor approval queue and suspension, specialty CRUD, overview and appointment statistics.

## 3. Domain model

```
ApplicationUser (ASP.NET Identity: email, password, names, gender, DOB, IsActive)
├── PatientProfile    blood type, allergies, chronic conditions, emergency contact
├── DoctorProfile     specialty, bio, experience, fee, clinic address/city,
│   │                 IsApprovedByAdmin, AverageRating, TotalReviews (cached)
│   ├── DoctorSchedule   weekly template: DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive
│   └── AppointmentSlot  a concrete bookable slot: Date, StartTime, EndTime, Status (Available/Booked/Blocked)
├── Notification      Title, Message, Type, IsRead, RelatedEntityId (usually an appointment id)
└── RefreshToken      hashed token, expiry, rotation chain, revocation info

Appointment   Patient ↔ Slot ↔ Doctor; Status, ReasonForVisit, DoctorNotes, cancellation info, ReminderSentAt
Review        one per completed appointment; Rating 1–5, Comment, IsVisible
Specialty     Name (unique), Description, IconUrl, IsActive
```

**Relationships to know:**
- A slot can have **several** appointments over time. After a cancellation the slot is freed and can be booked again, and the cancelled appointment is kept as history.
- `Notification.RelatedEntityId` is not a foreign key. For appointment notifications it holds the appointment id.
- Enum values (appointment status, slot status, cancelled-by, gender, blood type) are stored as strings and sent as strings over the API.

### Appointment lifecycle

```
            book (patient)
 [slot Available] ───────────────► Confirmed ──── complete (doctor) ───► Completed ──► review (patient, once)
                                       │
                                       ├── cancel (patient ≥2h before start; doctor or admin anytime) ──► Cancelled
                                       │                       (slot → Available, or Blocked if the schedule no longer covers it)
                                       └── mark no-show (doctor or admin, ≥15 min after start) ──► NoShow
```

`Pending` exists in the enum but no code path sets it. `Cancelled`, `Completed` and `NoShow` are final.

## 4. Business rules

| Area | Rule |
|---|---|
| Doctor visibility | Patients see only doctors that are approved **and** active: search, the public profile (404 otherwise), slots (empty otherwise) and booking (404 otherwise). Unapproved doctors can still log in and set up their schedules |
| Schedules | One schedule per doctor per weekday. Creating a second active one for the same day is rejected (409); an inactive one for that day is reused. Deleting a schedule is refused (409) while upcoming appointments are booked on it; a schedule with only past appointments is deactivated instead of deleted, to keep that history |
| Slot generation | Slots are generated for the next 14 days, only whole slots that end by `EndTime`, and only for active schedules. A daily job (`SlotGenerationJob`) rolls every active schedule forward, so the window never runs out. Generation is idempotent: it skips times that overlap an existing slot. Updating a schedule deletes future free slots and regenerates them in one transaction, keeping booked slots and slots an appointment points to |
| Slot times | Dates and times are the clinic's local wall-clock time and carry no time zone. The clinic's zone is `Clinic:TimeZoneId` (default `Africa/Cairo`), and every rule that compares a slot with "now" or "today" uses the clinic's clock |
| Booking | The slot must exist, be `Available`, and not be in the past. A conditional `UPDATE … WHERE Status = Available` inside a transaction guarantees that only one booking succeeds |
| Patient cancellation | Allowed only for the patient's own appointment, only if it is not already cancelled, completed or a no-show, and only up to 2 hours before the start |
| Doctor / admin cancellation | A doctor can cancel their own appointments, an admin any appointment, with no time limit |
| Completion | Only the owning doctor, and only once the appointment has started. Not allowed for cancelled, already-completed or no-show appointments |
| No-show | The owning doctor or an admin marks a confirmed appointment as `NoShow`, no earlier than 15 minutes after its start; the patient is notified. Nothing is marked automatically |
| Doctor notes | Only the owning doctor. Not allowed on cancelled appointments |
| Reviews | Only the patient who owns a **completed** appointment, at most one review per appointment. Rating 1–5, comment ≤ 300 characters. Only the author or an admin can delete a review |
| Appointment details | Readable only by the owning patient, the owning doctor, or an admin |
| Reminders | Run hourly. For confirmed appointments starting in the next 24 hours, the job sends an email and then records the reminder. A failed email is retried on the next run |
| Login | Wrong passwords count toward lockout: 5 failures lock the account for 15 minutes. Suspended or unconfirmed accounts cannot log in |
| Sessions | Access token: 15 minutes. Refresh token: 7 days, rotated on every use. Reusing an already-rotated refresh token revokes all of that user's sessions, unless it comes from the same IP within 5 minutes, which is treated as a client race |
| Suspension | Suspending a doctor deactivates the account and revokes their refresh tokens. Their current access token stays valid until it expires |
| Password reset | Revokes all of the user's refresh tokens |

Known gaps in these rules are tracked in [REVIEW.md](REVIEW.md) with their status.

## 5. Architecture

Clean Architecture, one project per layer:

```
ClinicFlow (host: DI, middleware, background job, startup seeding)
 ├── Presentation        controllers, FluentValidation validators, ValidationFilter
 │    └── Services.Abstraction   service interfaces
 ├── Services            business logic, AutoMapper profiles, JWT
 │    └── Persistence    EF Core DbContext, configurations, migrations, repositories, UnitOfWork, DataSeeder
 ├── Infrastructure      adapters for outside services: email (MailKit)
 └── Domain              entities, enums, repository interfaces
Shared                   DTOs, Result/Error types, error catalogues, settings
```

### Request flow

```
HTTP → forwarded headers → exception handler / status pages → Serilog request log → HTTPS redirect → CORS → rate limiter → JWT auth → authorization
     → controller (Presentation)
        → ValidationFilter: runs the matching IValidator<T>; 400 on failure
        → service (Services): returns Result / Result<T>; expected failures are Errors, never exceptions
           → IUnitOfWork → repositories → EF Core → SQL Server
     ← ApiController.HandleResult: Result → 200/201/204, or Error → ProblemDetails with status by ErrorType
```

### Key design choices

| Choice | Where / why |
|---|---|
| Result pattern | Services return `Result`/`Result<T>`. `ErrorType` maps to the HTTP status in one place (`ApiController`) |
| Unit of Work + repositories | Services use `IUnitOfWork` only; repositories are created lazily per request |
| Double-booking protection | A conditional `ExecuteUpdate` on the slot inside a transaction |
| Side effects after commit | Notification and email failures are logged and never fail the main operation |
| Refresh-token rotation | Tokens are stored as SHA-256 hashes. Each refresh issues a new token and revokes the old one; reuse is detected |
| Startup | Migrations run automatically. Roles and specialties are always seeded; demo data only in Development or when enabled explicitly |

## 6. Technology

| Concern | Technology |
|---|---|
| Runtime / web | .NET 10, ASP.NET Core 10 (controllers) |
| Data | Entity Framework Core 10, SQL Server 2022 |
| Identity / auth | ASP.NET Core Identity, JWT bearer (HS256), rotating refresh tokens |
| Validation / mapping | FluentValidation 12, AutoMapper 16 |
| Email | MailKit (SMTP, StartTLS) |
| Logging | Serilog: console, rolling file, Seq |
| API docs | ASP.NET Core OpenAPI + Scalar (`/scalar` in Development) |
| Tests | xUnit + NSubstitute (unit); WebApplicationFactory + Testcontainers SQL Server (integration) |
| Containers | Multi-stage Linux Dockerfile, `docker-compose.yml` (db + backend + frontend) |

## 7. Glossary

| Term | Meaning |
|---|---|
| Schedule | A doctor's weekly working-hours template for one weekday |
| Slot | One concrete bookable time window generated from a schedule |
| Approval | An admin's sign-off that makes a doctor visible in search |
| Suspension | An admin deactivating a doctor's account |
| Rotation | Exchanging a refresh token for a new pair; the old token becomes unusable |
| Demo data | The seeded sample clinic (doctors, patients, appointments) with published passwords; Development only |
