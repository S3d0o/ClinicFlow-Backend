# ClinicFlow — Project Overview

ClinicFlow is a clinic-booking platform. Patients find doctors and book appointment slots. Doctors publish weekly schedules and manage their appointments. Admins approve doctors and manage the specialty catalogue.

This repository is the **backend**: a .NET 10 REST API. A separate React app is the frontend. The two apps agree on the API through [`API_CONTRACT.md`](API_CONTRACT.md).

**This is the `v2` branch, at the end of P0** (tag `v2.0.0-p0`). It has V1's features on its own database (`ClinicFlowV2`), rebuilt on the foundation V2 grows from ([plans/P0.md](v2/plans/P0.md)):
- tenancy (one organization, one branch)
- branch time zones with UTC slot times
- the appointment state machine and CQRS-lite
- permissions
- an outbox for every notification and email
- an audit trail
- health probes and a deploy-time migration step

No `/api/v2` endpoint exists yet; that starts in P1. V1 lives on `master` and is unchanged.

| Document | Purpose |
|---|---|
| [V1_GUIDE.md](V1_GUIDE.md) | **The complete V1 guide:** every flow end to end, with diagrams; start here |
| **PROJECT_OVERVIEW.md** (this file) | What the system does, its rules, and how it is built |
| [API_CONTRACT.md](API_CONTRACT.md) | The shared backend ↔ frontend contract: endpoints, payloads, errors, auth |
| [openapi.json](openapi.json) | Machine-readable OpenAPI 3.1 document generated from the running API |
| [SETUP_AND_DEPLOYMENT.md](SETUP_AND_DEPLOYMENT.md) | Running locally, configuration, Docker, production checklist |
| [REVIEW.md](REVIEW.md) | Code review: known issues, their status and fix risk |
| [v2/README.md](v2/README.md) | V2 plan and design: clinic operations, roles and permissions, EMR, billing, insurance, roadmap; [v2/plans/P0.md](v2/plans/P0.md) is the finished foundation phase |
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
- Both parties get an in-app notification, and the patient gets a confirmation email. Like every notification and email, they go out through the outbox about 2 seconds after the booking commits.

**Cancellation:**
- A patient can cancel up to 2 hours before the start time.
- The doctor and admins can cancel at any time.
- The slot becomes bookable again, unless the doctor's schedule no longer covers it (then it is blocked).

**Completion and reviews:**
- The doctor marks a visit as completed and can add notes.
- The patient can then leave one review (1–5 stars, optional comment).
- The doctor's average rating is recalculated.

**Reminders:**
- An hourly background job finds confirmed appointments that start within the next 24 hours, marks them as reminded and queues a reminder for each; the outbox sends the email and the notification.

**Notifications:**
- A per-user in-app inbox with read/unread state.

**Accounts:**
- Email confirmation, password reset by email, profile management.
- JWT access tokens with rotating refresh tokens, and login lockout.

**Admin:**
- Doctor approval queue and suspension, specialty CRUD, overview and appointment statistics.

## 3. Domain model

```
Organization (one in V2) ── Branch (MAIN; its IANA time zone)

ApplicationUser (ASP.NET Identity: email, password, names, gender, DOB, IsActive)
├── PatientProfile    blood type, allergies, chronic conditions, emergency contact
├── DoctorProfile     specialty, bio, experience, fee, clinic address/city,
│   │                 IsApprovedByAdmin, AverageRating, TotalReviews (cached)
│   ├── DoctorSchedule   weekly template: DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive
│   └── AppointmentSlot  a concrete bookable slot: Date, StartTime, EndTime (the branch's wall clock),
│                        StartsAtUtc, EndsAtUtc (the same instants in UTC), Status (Available/Booked/Blocked)
├── Notification      Title, Message, Type, IsRead, RelatedEntityId (usually an appointment id)
└── RefreshToken      hashed token, expiry, rotation chain, revocation info

Appointment     Patient ↔ Slot ↔ Doctor; Status (changed only by its own methods), ReasonForVisit, DoctorNotes,
                cancellation info, CheckedInAt/StartedAt/CompletedAt/NoShowAt, RescheduledFromId, ReminderSentAt
Review          one per completed appointment; Rating 1–5, Comment, IsVisible
Specialty       Name (unique), Description, IconUrl, IsActive

OutboxMessage   a domain event waiting for delivery (notifications, emails), with retries and a dead letter
MessageDelivery which channel of which message has been sent, so a retry never sends twice
AuditEntry      who changed which record, from where, old/new values; append-only
```

Everything the clinic creates is **organization-owned** (`OrganizationId`, filtered on every query), and schedules, slots and appointments are also **branch-owned**. People and reference data (users, patient profiles, specialties) are global.

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

The rules live in the `Appointment` entity (ADR 0004), which also knows the V2 steps no route uses yet: `Confirmed → CheckedIn → InProgress → Completed`, `NoShow → Confirmed` (a same-day correction), and `Confirmed → Rescheduled` (linked to its replacement). Any other move returns 409 `Appointment.InvalidStatusTransition`.

## 4. Business rules

| Area | Rule |
|---|---|
| Doctor visibility | Patients see only doctors that are approved **and** active: search, the public profile (404 otherwise), slots (empty otherwise) and booking (404 otherwise). Unapproved doctors can still log in and set up their schedules |
| Schedules | One schedule per doctor per weekday. Creating a second active one for the same day is rejected (409); an inactive one for that day is reused. Deleting a schedule is refused (409) while upcoming appointments are booked on it; a schedule with only past appointments is deactivated instead of deleted, to keep that history |
| Slot generation | Slots are generated for the next 14 days, only whole slots that end by `EndTime`, and only for active schedules. A daily job (`SlotGenerationJob`) rolls every active schedule forward, so the window never runs out. Generation is idempotent: it skips times that overlap an existing slot. Updating a schedule deletes future free slots and regenerates them in one transaction, keeping booked slots and slots an appointment points to |
| Slot times | A slot's date and times are its branch's wall-clock time (what people see), and `StartsAtUtc` / `EndsAtUtc` are the same instants in UTC, computed from the branch's zone (default `Africa/Cairo`) when the slot is generated. Daylight saving: a skipped time gets no slot, and a repeated time is its first occurrence. Every rule that compares a slot with "now" uses the UTC start; "today" is the branch's date. Every timestamp the API sends is UTC with `Z` |
| Booking | The slot must exist, be `Available`, and not be in the past. A conditional `UPDATE … WHERE Status = Available` inside a transaction guarantees that only one booking succeeds |
| Patient cancellation | Allowed only for the patient's own appointment, only if it is not already cancelled, completed or a no-show, and only up to 2 hours before the start |
| Doctor / admin cancellation | A doctor can cancel their own appointments, an admin any appointment, with no time limit |
| Completion | Only the owning doctor, and only once the appointment has started. Not allowed for cancelled, already-completed or no-show appointments |
| No-show | The owning doctor or an admin marks a confirmed appointment as `NoShow`, no earlier than 15 minutes after its start; the patient is notified. Nothing is marked automatically |
| Doctor notes | Only the owning doctor. Not allowed on cancelled, pending or rescheduled appointments. Audited as "changed" only: the text is never copied into the audit trail |
| Reviews | Only the patient who owns a **completed** appointment, at most one review per appointment. Rating 1–5, comment ≤ 300 characters. Only the author or an admin can delete a review |
| Appointment details | Readable only by the owning patient, the owning doctor, or an admin |
| Reminders | Run hourly. For confirmed appointments starting in the next 24 hours, the job records the reminder and queues it in the same save; the outbox sends it, retrying a failed email with backoff |
| Notifications and emails | Never sent inside a request. The change and its event are saved together, so a message goes out if, and only if, the change commits; delivery is at least once, and a channel already sent is never sent again |
| Audit | Every booking, appointment transition and note, doctor approval or profile change, suspension and role change writes an append-only audit entry (who, IP, old/new values, request) in the same transaction. The app's database login can't update or delete entries |
| Login | Wrong passwords count toward lockout: 5 failures lock the account for 15 minutes. Suspended or unconfirmed accounts cannot log in |
| Sessions | Access token: 15 minutes. Refresh token: 7 days, rotated on every use. Reusing an already-rotated refresh token revokes all of that user's sessions, unless it comes from the same IP within 5 minutes, which is treated as a client race |
| Suspension | Suspending a doctor deactivates the account and revokes their refresh tokens. Their current access token stays valid until it expires |
| Password reset | Revokes all of the user's refresh tokens |

Known gaps in these rules are tracked in [REVIEW.md](REVIEW.md) with their status.

## 5. Architecture

Clean Architecture, one project per layer:

```
ClinicFlow (host: DI, middleware, background jobs, health probes, startup seeding)
 ├── Presentation        controllers, validators, ValidationFilter, [HasPermission], ICurrentUser over HTTP
 ├── Services            command services, outbox event handlers, AccessGuard, AutoMapper profiles
 ├── Persistence         DbContext, configurations, migrations, repositories, UnitOfWork, query classes (reads),
 │                       interceptors (tenant, outbox, audit), outbox processor, transaction runner, DataSeeder
 ├── Infrastructure      adapters for outside services: email (MailKit), JWT creation
 ├── Services.Abstraction   service and query interfaces, IClock, ICurrentUser, IAccessGuard
 └── Domain              entities with their rules, domain events, Result/Error, repository interfaces
Shared                   DTOs, error catalogues, roles and permissions, settings
```

`Services` doesn't reference `Persistence`; both depend only on abstractions, and `LayerDependencyTests` fail the build on any layer using a type it mustn't (ADR 0006).

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
| CQRS-lite | Command services load tracked entities, call their methods and save; query classes in Persistence project straight to response DTOs with `AsNoTracking`. One database, no MediatR (appointments first) |
| Status rules in the entity | `Appointment` changes its status only through methods that return `Result` (ADR 0004) |
| Double-booking protection | A conditional `ExecuteUpdate` on the slot inside a transaction |
| Transient SQL errors | Retried (`EnableRetryOnFailure`); manual transactions run through one runner that rewinds the change tracker between attempts |
| Transactional outbox | Domain events are saved with the change and delivered by a background dispatcher with retries, backoff and a dead letter (06 §5) |
| Audit interceptor | Audited types opt in from their EF configuration; entries are written in the change's transaction (06 §5.1) |
| Tenancy | A query filter and a save interceptor keep every query and write inside the current organization (ADR 0001) |
| One clock | "Now" comes only from `IClock`; a test fails the build on any other read of the system clock |
| Permissions | New endpoints use `[HasPermission]` and `IAccessGuard` against the default matrix (ADR 0002); inherited endpoints keep their roles |
| Refresh-token rotation | Tokens are stored as SHA-256 hashes. Each refresh issues a new token and revokes the old one; reuse is detected |
| Startup | Migrations run at startup only in Development (`Database:MigrateOnStartup`); deployments run the migration bundle first, and startup refuses a database with pending migrations. Roles and specialties are always seeded; demo data only in Development or when enabled explicitly |
| Probes | `/health/live` for restarts, `/health/ready` (database, outbox lag) for routing |

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
| Containers | Multi-stage Linux Dockerfile with an EF migration bundle; `docker-compose.yml` (db → db-init → migrate → backend, + frontend) |

## 7. Glossary

| Term | Meaning |
|---|---|
| Schedule | A doctor's weekly working-hours template for one weekday |
| Slot | One concrete bookable time window generated from a schedule |
| Approval | An admin's sign-off that makes a doctor visible in search |
| Suspension | An admin deactivating a doctor's account |
| Rotation | Exchanging a refresh token for a new pair; the old token becomes unusable |
| Demo data | The seeded sample clinic (doctors, patients, appointments) with published passwords; Development only |
