# ClinicFlow Backend — Code Review

Reviewed: branch `unit-integration-tests` at `876bbde`, 2026-10-09. This review is read-only: no code was changed. The solution builds with 0 errors and 13 warnings. Tests were not run for this review.

**How to read this report**
- **Severity** is how much damage the problem can do as the code stands today.
- **Fix risk** is how likely the fix is to break something else:
  - *Low*: local change, little chance of breaking callers.
  - *Medium*: touches shared behaviour, an API contract or data semantics, so it needs tests or a frontend check.
  - *High*: needs a migration, a product decision, or changes many call sites.
- File references are `path:line` from the repo root.

## Fix status (updated 2026-10-09)

After the review, a fix pass was made on branch `docs/review-and-claude-md`. Its scope: safe fixes that change no API contract. Every finding below is kept as written. Each one now has a status: a **Status** line under the Critical, High and Medium items, and a **Status** column in the Low tables. The `path:line` references describe the code *before* the fixes.

| Status | Meaning |
|---|---|
| ✅ Fixed | Resolved in the named commit |
| 🟡 Partly fixed | The main problem is resolved; the status says what remains |
| ⏸ Open | Not attempted, with the reason: a product decision, an API change the frontend would see, or a larger refactor |

| Commit | Contents |
|---|---|
| `9035a7d` | Vulnerable and outdated `Microsoft.*` and Testcontainers packages, build warnings, misleading cancel tests |
| `d3b6511` | Authorization, token, login and email security |
| `3a2c391` | Schedules, appointments, data access |
| `e5cc0db` | Startup, configuration, seeding, reminder job, Docker |

**Verification after the fixes:**
- `dotnet build --no-incremental`: 0 warnings. Nullable warnings are now errors.
- Unit tests: 36 passed (17 before).
- Integration tests: 31 passed (17 before).
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

**Totals:**
- Critical: 4 of 4 fixed.
- High: 5 of 8 fixed (H4, H6 and H8 are open).
- Medium: 6 fixed, 1 partly fixed, 8 open.
- .NET High/Medium: 4 fixed, 2 partly fixed, 1 open.

**Left open on purpose:**
- **Product decisions:** H4, H8, M1, M2, M14.
- **API changes the frontend would see:** H6, M3, M5, M13, M15, L6, NM5, NL2, NL9, NL11.
- **Larger work:** M12, NL14, NL21, §4.

---

## 1. Architecture

### 1.1 Projects and how they actually reference each other

| Project | Purpose | Project references (from `.csproj`) |
|---|---|---|
| `Domain` | Entities, enums, repository interfaces, `IUnitOfWork`, filter params | none, but references the **EF Core Identity package** (`Domain/Domain.csproj:14`) |
| `Shared` | DTOs, `Result`/`Error`, error catalogs, settings | `Domain` (`Shared/Shared.csproj:14`) |
| `Persistance` (`Persistence.csproj`) | `ClinicDbContext`, configurations, migrations, repositories, `UnitOfWork`, `DataSeeder` | `Domain` |
| `Services.Abstraction` | Service interfaces | `Shared` |
| `Services` | Service implementations, AutoMapper profiles, email, JWT | `Domain`, **`Persistence`** (`Services/Services.csproj:11`), `Services.Abstraction` |
| `Presentation` | Controllers, validators, `ValidationFilter` | `Services.Abstraction` |
| `ClinicFlow` | Host: DI, middleware, `AppointmentReminderJob` | `Persistence`, `Presentation`, `Services` |
| `ClinicFlow.Tests` | Unit tests (xUnit + NSubstitute) | `Domain`, `Services`, `Shared` |
| `ClinicFlow.IntegrationTests` | API tests (WebApplicationFactory + Testcontainers SQL Server) | `ClinicFlow` |

```
                    ┌──────────────┐
                    │  ClinicFlow  │  host, DI, middleware, reminder job, seeding
                    └──┬────┬────┬─┘
          ┌────────────┘    │    └──────────────┐
          ▼                 ▼                   ▼
   ┌─────────────┐   ┌───────────┐       ┌──────────────┐
   │ Presentation│   │ Services  │──────►│ Persistence  │
   └──────┬──────┘   └─┬───────┬─┘       └──────┬───────┘
          │            │       │                │
          ▼            ▼       │                ▼
   ┌──────────────────────┐    │          ┌──────────┐
   │ Services.Abstraction │    └─────────►│  Domain  │◄── EF Core / Identity package
   └──────────┬───────────┘               └──────────┘
              ▼                                 ▲
         ┌─────────┐                            │
         │ Shared  │────────────────────────────┘
         └─────────┘
```

The layering is looser than "Clean Architecture" suggests:
- `Domain` depends on EF Core. `IUnitOfWork` exposes `IDbContextTransaction` (`Domain/Interfaces/IUnitOfWork.cs:2,20`).
- `IReviewRepo` lives in the Domain project but is declared in namespace `Persistence.Repositories` (`Domain/Interfaces/IRepositories/IReviewRepo.cs:2`).
- `Services` references the concrete `Persistence` project.

### 1.2 Request data flow

```
HTTP request
  → Serilog request logging (Program.cs:59)
  → HTTPS redirect → CORS "React" (localhost:3000 only) → JWT authentication → authorization
  → MVC routing (controllers come from the Presentation assembly via AddApplicationPart)
  → ValidationFilter: looks up IValidator<T> for each action argument; returns 400 ValidationProblemDetails on failure
  → Controller: reads userId from ClaimTypes.NameIdentifier and role from ClaimTypes.Role
  → Service (Services/Implementations)
       uses IUnitOfWork (lazy repositories + SaveChangesAsync/BeginTransactionAsync),
       AutoMapper, INotificationService, IEmailService (MailKit SMTP), IDateTimeProvider
  → Repository → EF Core → SQL Server
  ← Result / Result<T>
  ← ApiController.HandleResult: maps ErrorType to an HTTP status (Validation 400, NotFound 404,
     Unauthorized/InvalidCredentials 401, Forbidden 403, Conflict 409, Failure 500)
```

**Side flows**
- **Startup:** `DataSeeder.SeedAsync` runs `Database.MigrateAsync()`, then seeds a full demo dataset in every environment (`ClinicFlow/Program.cs:55`).
- **Background job:** `AppointmentReminderJob` runs at startup and then every hour. It creates in-app notifications and sends emails for confirmed appointments starting within the next 24 hours.
- **Booking concurrency:** `SlotRepo.SetStatusFromAvailableToBookedAsync` runs a conditional `ExecuteUpdate WHERE Status = Available` inside a transaction. If 0 rows are affected, the slot was already taken. The `RowVersion` column on `AppointmentSlot` is **not** what prevents double-booking.
- **Auth:** ASP.NET Identity stores users. Access tokens are JWTs (15 min). Refresh tokens are SHA-256 hashed, rotated on every use, carry a security stamp, and use a `RowVersion` for concurrent-use detection.

### 1.3 Domain model notes not obvious from the README
- `AppointmentSlot` → `Appointment` is **one-to-many**, so a slot can be rebooked after a cancellation and the history is kept (`Persistance/AppData/Configurations/AppointmentConfig.cs:16-20`).
- `Notification.RelatedEntityId` is deliberately not a foreign key.
- Enum values are stored as strings: appointment status, cancelled-by, slot status, gender, blood type.
- Doctor schedules are weekly templates. Slots are pre-generated rows, created when a schedule is created or updated.

---

## 2. README: outdated, wrong, or missing

**Status:** ✅ Fixed. `README.md` was rewritten to match the code (correct Docker command, Linux image, double-booking mechanism, routes, solution file, ports, project structure; the MIT claim was removed because there is no LICENSE file). The missing topics (tests, docker-compose and `.env`, configuration keys, demo logins, frontend URL, slot window, cancellation rule) now live in `docs/SETUP_AND_DEPLOYMENT.md`, `docs/PROJECT_OVERVIEW.md` and `docs/API_CONTRACT.md`. The tables below describe the old README.

### Wrong

| README says | Reality |
|---|---|
| `docker build -f ClinicFlow/Dockerfile ...` | The Dockerfile is at the repo root: `docker build -t clinicflow-backend .` |
| Docker image is "Windows Nano Server" | The Dockerfile uses Linux `sdk:10.0`/`aspnet:10.0` images. `ClinicFlow.csproj:8` still says `DockerDefaultTargetOS=Windows`. |
| "optimistic concurrency (`RowVersion`) prevents double-bookings" | A conditional `ExecuteUpdate` prevents them (`Persistance/Repositories/SlotRepo.cs:48-54`). |
| A "TheftDetectionWindow invalidates the entire token family" | The theft-detection branch can never run, and reuse of a rotated token never revokes its descendants. See H7. |
| Seeding creates "roles and a default admin account" | It seeds roles, 10 specialties, an admin, 7 doctors, 5 patients, schedules, slots, appointments, reviews and notifications, all with **published passwords**. See C2. |
| Doctors "confirm" appointments | There is no confirm endpoint. Booking creates `Confirmed` directly, and `Pending` is never used. |
| Doctors must be approved "then publish their schedules" | Nothing enforces approval. See M1. |
| Reminder covers "appointments scheduled for tomorrow" | It covers appointments starting within the next 24 hours (`AppointmentRepo.cs:97-111`). |
| `ClinicFlow.sln` | The solution file is `ClinicFlow.slnx`. |
| "Dependency flow: ClinicFlow → Presentation → Services.Abstraction / → Services → Persistance → Domain" | `Presentation` does not reference `Services`, and `Shared` references `Domain`. See §1.1. |
| Run at `https://localhost:5143/scalar` | 5143 is the HTTP port. HTTPS is 7129 (`ClinicFlow/Properties/launchSettings.json`). The README JWT issuer example uses 5143; `appsettings.json` uses 7129. |
| `ClinicFlow/Dockerfile` in the structure tree, root named `ClinicFlow-master/` | The tree is stale. |
| License: "[MIT License](LICENSE)" | There is no LICENSE file in the repo. |

### API table mismatches

| README | Actual route |
|---|---|
| `DELETE /api/appointments/{id}` (Patient/Doctor) | `POST /api/appointments/{id}/cancel` (Patient/Doctor/**Admin**) |
| `PATCH /api/appointments/{id}/notes` | `PUT /api/appointments/{id}/notes` |
| `GET /api/appointments` | `GET /api/appointments/my` (patient) and `GET /api/appointments/doctor/my` (doctor) |
| `GET /api/reviews` | `GET /api/reviews/doctor/{doctorId}` **and** `GET /api/doctors/{id}/reviews` |
| `PATCH /api/notifications/{id}/read` | `POST /api/notifications/{id}/read` |
| `GET /api/admin/overview` ("revenue") | `GET /api/admin/stats/overview`, which returns no revenue figure |
| `GET /api/specialties` | `GET /api/Specialty` (from the `[controller]` token) and `GET /api/admin/specialties` |

### Missing
- **Endpoints not listed:**
  - `POST /api/auth/logout`
  - `POST /api/appointments/{id}/complete`
  - `GET /api/appointments/{id}`
  - `GET` / `DELETE /api/doctors/schedule[/{id}]`
  - `PUT /api/doctors/profile`
  - `GET` / `PATCH /api/patients/profile`
  - `PATCH /api/profile/me`
  - `POST /api/notifications/read-all`
  - `DELETE /api/reviews/{id}`
  - `POST /api/admin/doctors/{id}/suspend`
  - `GET /api/admin/stats/appointments`
  - Specialty CRUD
- **Tests:** there is no section on the two test projects, or on the fact that integration tests need Docker.
- **`docker-compose.yml`:** not mentioned, nor its required `.env` variables (`DB_PASSWORD`, `JWT_SECRET`, `EMAIL_USERNAME`, `EMAIL_PASSWORD`).
- **Config keys:** `appsettings.json` has **no** `ConnectionStrings` section or `JwtSettings:Secret`, so they must come from user-secrets, `appsettings.Development.json` (git-ignored) or the environment. `EmailSettings:FromAddress`, `Username` and `Password` are also required. `JwtSettings:TheftDetectionWindowMinutes` is not documented.
- **Seeded logins:** the demo accounts and their passwords are not listed (they are in `Persistance/AppData/DataSeeder.cs:13-26`).
- **Hardcoded frontend:** CORS and email links point at `http://localhost:3000`.
- **Slot generation:** slots are only generated 14 days ahead.
- **Cancellation rule:** the 2-hour cancellation window isn't documented.

---

## 3. Code-quality problems, ranked by severity

### Critical

#### C1. Specialty create, update and delete are open to anonymous callers
- **Status:** ✅ Fixed in `d3b6511`. `[Authorize(Roles = "Admin")]` is restored on create, update and delete. Covered by `AuthorizationTests` (anonymous → 401, patient → 403).
- **Where:** `Presentation/Controllers/SpecialtyController.cs:37`, `:50`, `:63`. `[Authorize(Roles = "Admin")]` is commented out on all three. The controller has no class-level `[Authorize]`.
- **Impact:** anyone on the internet can create, rename or delete specialties via `/api/Specialty`. Deletion is blocked only while doctors are assigned. The same operations already exist, correctly protected, under `AdminController` (`AdminController.cs:52-87`).
- **Fix risk:** Low. Restore the attributes, or delete the write actions from `SpecialtyController`. First confirm whether the frontend calls `/api/Specialty` for writes.

#### C2. Demo accounts with published passwords are created in every environment, including Production
- **Status:** ✅ Fixed in `e5cc0db`. The demo dataset is seeded only when `Seeding:DemoData` is true (default: Development only). Roles and specialties are always seeded. A real admin can be bootstrapped from `Seeding:AdminEmail` / `Seeding:AdminPassword` (compose passes `ADMIN_EMAIL` / `ADMIN_PASSWORD`). **Databases that were already seeded still contain the demo accounts:** delete them or change their passwords.
- **Where:** `ClinicFlow/Program.cs:55` calls `DataSeeder.SeedAsync` unconditionally. The accounts are listed in `Persistance/AppData/DataSeeder.cs:13-26`, and the admin is created at `:115-127` with password `Admin@1234`.
- **Impact:** `docker-compose.yml` runs with `ASPNETCORE_ENVIRONMENT=Production`, and a fresh deployment gets `admin@clinicflow.com / Admin@1234`. Both are visible in the source.
- **Fix risk:** Medium. Gate demo data behind `IsDevelopment()` or a config flag, and provide a separate bootstrap-admin path (env-provided credentials). Integration tests are unaffected, because they reset the DB per class.

#### C3. Password-reset tokens are written to the logs
- **Status:** ✅ Fixed in `d3b6511`. The log line is gone; only the user id is logged.
- **Where:** `Services/Implementations/AuthService.cs:295-297`. It logs `"[DEV ONLY] Password reset token ..."` at Information level with no environment check.
- **Impact:** logs go to Console, rolling files and Seq (`ClinicFlow/appsettings.json:10-24`). Anyone with log access can reset any user's password, including the admin's.
- **Fix risk:** Low. Delete the statement.

#### C4. Any authenticated user can read any appointment, including clinical notes
- **Status:** ✅ Fixed in `d3b6511`. Only the owning patient, the owning doctor or an admin can read an appointment; anyone else gets 403. Covered by `GetAppointmentAsyncTests` and `AuthorizationTests`.
- **Where:** `Presentation/Controllers/AppointmentController.cs:67-75` (`[Authorize]` only) → `Services/Implementations/AppointmentService.cs:358-367` (no ownership check).
- **Impact:** appointment IDs are sequential ints. Any logged-in patient can enumerate `GET /api/appointments/{id}` and read other patients' names, reason for visit and `DoctorNotes` (`Shared/DTOs/Appointment/AppointmentResponse.cs:12-19`). This is a medical-data exposure (IDOR).
- **Fix risk:** Low–Medium. Pass the caller's id and role, then allow only the owning patient, the owning doctor or an admin. Check what the frontend uses this endpoint for.

### High

#### H1. A doctor can hang the server with a late-evening schedule
- **Status:** ✅ Fixed in `3a2c391`. The loop counts minutes since midnight and creates only whole slots that end by `EndTime`, so it can no longer wrap past midnight or overrun the end time. Covered by `CreateScheduleAsyncTests`.
- **Where:** `Services/Implementations/DoctorService.cs:155-169`. `while (currentTime < schedule.EndTime)` uses `TimeOnly.AddMinutes`, which **wraps at midnight**.
- **Impact:** with `StartTime=22:30`, `EndTime=23:59`, `SlotDurationMinutes=60`, the loop goes 22:30 → 23:30 → 00:30 (still < 23:59) and never stops. It keeps allocating slots until the process runs out of memory. The validator allows these values (`Presentation/Validators/CreateScheduleRequestValidator.cs:16-28`). Any registered doctor can trigger it (see M1: approval isn't required).
- **Related bug:** the same loop lets the last slot overrun `EndTime` (09:00–09:45 with 30-min slots produces a 09:30–10:00 slot). The seeder uses the correct condition (`DataSeeder.cs:539`).
- **Fix risk:** Low. Loop while `current.AddMinutes(d) <= EndTime` and guard against wrap-around, for example by working in `TimeSpan` minutes.

#### H2. Doctors (and admins) can delete any review
- **Status:** ✅ Fixed in `d3b6511`. Only the review's author or an admin can delete it. Covered by `DeleteReviewAsyncTests`. Still open: whether admins should hide reviews (`IsVisible`) instead of deleting them.
- **Where:** `Services/Implementations/ReviewService.cs:69` checks ownership only when `role == "Patient"`. `ReviewController.cs:57-68` allows any authenticated role.
- **Impact:** a doctor can delete negative reviews of themselves or of competitors. The rating cache is then recalculated, so the doctor's average rating changes too.
- **Fix risk:** Low. Allow the owning patient or an admin only. Decide whether admins should hide reviews (`IsVisible`) rather than delete them.

#### H3. Suspending a user doesn't end their session; refresh ignores `IsActive`
- **Status:** ✅ Fixed in `d3b6511`. Refresh rejects inactive users, and suspending a doctor revokes their active refresh tokens. The misleading comment is corrected. Access tokens stay valid until they expire (15 min). Covered by `AuthSecurityTests`.
- **Where:**
  - `Services/Implementations/TokenService.cs:81-86` loads the user but never checks `IsActive`.
  - `AdminService.cs:68` only sets `IsActive=false`.
- **Impact:** a suspended doctor keeps calling the API indefinitely by refreshing tokens. Login is blocked, but refresh is not.
- **Also wrong:** the comment at `AuthService.cs:326` ("invalidates all existing JWTs"). Access tokens carry no security stamp and stay valid until they expire.
- **Fix risk:** Low. Check `user.IsActive` in refresh, and revoke active refresh tokens on suspend.

#### H4. Editing or deleting a schedule fails with 500 once it has bookings, and edits are left half-applied
- **Status:** ⏸ Open. Needs a product decision about booked appointments.
- **Where:**
  - `DoctorService.cs:97-102` (update).
  - `DoctorService.cs:115-116` (delete).
  - `AppointmentSlotConfig.cs:16-17` (unique index on `DoctorProfileId, Date, StartTime`).
  - `DoctorScheduleConfig.cs:12` (deleting a schedule cascades to its slots) versus `AppointmentConfig.cs:20` (appointment→slot is `NoAction`).
- **Update failure:** the update saves the schedule first, then deletes future *available* slots, then regenerates **all** slots for 14 days. A regenerated slot that collides with a booked or blocked slot violates the unique index, so the request returns 500. By then the schedule change and the slot deletion are already committed. There is no transaction.
- **Delete failure:** a schedule whose slots have ever had an appointment can't be deleted. The cascade hits the `NoAction` FK and the request returns 500. `DoctorErrors.CannotDeleteScheduleWithBookedSlots` exists but is never used.
- **Fix risk:** Medium. Needs a product decision on what happens to booked appointments (keep them? notify?). Wrap the operation in a transaction, skip times that already have a slot, and soft-delete via `IsActive`.

#### H5. Partial schedule updates silently zero the fields you leave out
- **Status:** ✅ Fixed in `3a2c391`. Each member is mapped explicitly with a `PreCondition`. The `ForAllMembers(Condition(...))` fix suggested below does **not** work for nullable value types; a unit test showed it still writes `default`. The merged times are validated (`InvalidScheduleTime`, 400), and `UpdateScheduleRequest` no longer defaults `SlotDurationMinutes` / `IsActive`. Covered by `UpdateScheduleAsyncTests`.
- **Where:** `Services/MappingProfiles/DoctorProfile.cs:23-25` maps `UpdateScheduleRequest` (all-nullable, `Shared/DTOs/Doctor/UpdateScheduleRequest.cs:5-8`) without the `srcMember is not null` condition that the doctor- and patient-profile maps use.
- **Impact:** sending only `{ "startTime": "10:00" }` sets `EndTime` to `00:00`, and the schedule then generates zero slots. The validator (`UpdateScheduleRequestValidator.cs:17-21`) only compares the two times when both are present.
- **Fix risk:** Low. Add `.ForAllMembers(o => o.Condition(... is not null))`, then validate the merged values.

#### H6. Patient appointment list returns nothing by default (and 500s on bad paging)
- **Status:** ⏸ Open. The fix changes default paging results and adds new 400 responses (an API change), which this pass excluded.
- **Where:**
  - `Shared/DTOs/Appointment/AppointmentFilterRequest.cs:8-9`: `PageNumber` and `PageSize` have no defaults, so both are 0.
  - There is no validator for `AppointmentFilterRequest`.
  - `Persistance/Repositories/AppointmentRepo.cs:74-75` doesn't clamp the values.
- **Impact:**
  - `GET /api/appointments/my` with no query string runs `Take(0)` and returns an empty list.
  - `?PageNumber=0&PageSize=10` produces `Skip(-10)`, an exception and a 500.
  - The doctor version clamps to at least 1 (`AppointmentRepo.cs:41-42`), so by default doctors see exactly **one** appointment.
  - `PageSize` has no upper bound.
- **Fix risk:** Low. Default to 1/10, add a validator like `ReviewFilterRequestValidator`, and clamp in one shared helper.

#### H7. Refresh-token theft detection is unreachable, and token reuse isn't punished
- **Status:** ✅ Fixed in `d3b6511`. If a rotated token is reused from the same IP within `TheftDetectionWindowMinutes`, it is treated as a client race and rejected with 401. Any other reuse revokes **all** of the user's active refresh tokens (every session, not just one token chain). The dead branch is removed. Both paths are covered by `AuthSecurityTests`.
- **Where:** `Services/Implementations/TokenService.cs:60-78`.
- **Why it can't run:** `LastUsed` is only ever set during rotation (`:89`), and rotation also sets `Revoked` (`:92`). Any token with `LastUsed != null` has therefore already failed the `IsActive` check at `:60`, so lines `:63-78` are dead.
- **Impact:** replaying an old rotated token returns 401 but does **not** revoke the attacker's or the victim's newer tokens. The README claims family revocation.
- **Fix risk:** Medium. This is security-sensitive logic. On reuse of a rotated token, follow `ReplacedByTokenHash` and revoke the chain. Add integration tests first.

#### H8. Slots run out after two weeks
- **Status:** ⏸ Open. Needs a slot roll-forward job and idempotent generation; best done together with H4.
- **Where:** `DoctorService.cs:146-150` generates slots for days 0–13 only, and only when a schedule is created or updated. Nothing rolls the window forward.
- **Impact:** a doctor who sets a schedule once has no bookable slots 14 days later. The seeder generates 21 days and tops the demo slots up on every start, which hides the problem in demos.
- **Fix risk:** Medium. Add a daily background job that extends slots for active schedules, and make generation idempotent (skip existing times; see H4).

### Medium

#### M1. Doctor approval and suspension aren't enforced where it matters
- **Status:** ⏸ Open. Needs a decision on where the approval gate belongs.
- **Login:** `AuthService.cs:210-249` lets unapproved doctors log in, despite the comment at `:78`.
- **Schedules:** `DoctorService.cs:60-88` lets unapproved doctors create schedules and slots.
- **Public profile:** `DoctorController.cs:27-34` → `DoctorRepo.GetByIdAsync` (`DoctorRepo.cs:17-18`) returns unapproved doctors. The approval-filtered `GetDetailedByIdAsync` (`:9-16`) is never used.
- **Slots and booking:** `DoctorService.cs:128-139` and `AppointmentService.cs:17-42` don't check approval or `User.IsActive`, so slots of unapproved and suspended doctors are listed and bookable.
- **Fix risk:** Low–Medium. Decide where the gate belongs (login vs. per action), then add checks and tests.

#### M2. Slot times are local clinic time but are compared to UTC
- **Status:** ⏸ Open. Needs a clinic time-zone decision.
- **Where:**
  - `AppointmentService.cs:37-38` (booking a slot in the past).
  - `AppointmentService.cs:145-147` (2-hour cancellation window).
  - `AppointmentRepo.cs:97-111` (reminders).
  - `SlotRepo.cs:37` and `DoctorService.cs:146` ("today").
- **Impact:** slots like 09:00–13:00 are wall-clock times for an Egyptian clinic (UTC+2/+3) but are compared to `DateTime.UtcNow`. In summer, a patient can cancel a 10:00 appointment at 10:30 local time (07:30 UTC), *after* it started. Past slots stay bookable for 2–3 hours. Reminders and "today" are shifted the same way.
- **Fix risk:** Medium–High. Define a clinic time zone (config, or per doctor), convert at the boundaries, and route all "now" reads through `IDateTimeProvider` (see §4).

#### M3. Business errors return 500
- **Status:** ⏸ Open. Changes status codes the frontend may rely on (an API change).
- **Where:**
  - `Shared/Errors/AuthErrors.cs:16-17` (`EmailAlreadyExists`), `:22-26` (invalid confirmation/reset token), `:31-32` (`RegistrationFailed`, including Identity password-policy failures). All are `ErrorType.Failure`.
  - `DoctorErrors.cs:16` (`ScheduleDayAlreadyExists`; the controller documents 409).
  - `AppointmentErrors.cs:43`, `:51`.
  - `ApiController.cs:74` maps `Failure` to 500.
- **Impact:** a duplicate email at registration returns **500 Internal Server Error**. Monitoring sees false server errors, and clients can't tell a real outage from a user mistake.
- **Fix risk:** Medium. A one-line change per error, but it alters API status codes the frontend may depend on.

#### M4. Side effects after a commit can turn a success into a 500
- **Status:** ✅ Fixed in `d3b6511` and `3a2c391`. Failures in post-commit notifications (cancel by doctor or admin, complete, approve, suspend) and in the registration email are now logged, not returned to the client. Note: there is no endpoint to resend a confirmation email, so a user whose email failed still can't confirm.
- **Notifications:** `AppointmentService.cs:226-238` (cancel by doctor), `:265-277` (cancel by admin), `:316-321` (complete), and `AdminService.cs:41-46`, `:71-76`. These run *after* `SaveChangesAsync` without the try/catch used in `BookAppointmentAsync` and `CancelByPatientAsync`.
- **Registration email:** `AuthService.cs:86-88` and `:167-169` send the email after `CommitAsync`, inside the outer `try`. An SMTP failure jumps to `catch`, which calls `RollbackAsync()` on an already-committed transaction (`:97-101`, `:177-181`).
- **Impact:** the client gets an error and may retry. For registration, the user exists but got no email, and a retry returns "email already exists" (as a 500; see M3).
- **Fix risk:** Low. Wrap the side effects, or move them after the response / onto an outbox.

#### M5. No global exception handler
- **Status:** ⏸ Open. Changes the 500 response body; it belongs with NL9 (API shape).
- **Where:** `ClinicFlow/Program.cs:57-84`. There is no `UseExceptionHandler` or `AddProblemDetails`.
- **Impact:** unhandled exceptions (H4, H6, the review race in L8) return a bare 500, or the developer exception page in Development. The `ProblemDetails` contract used everywhere else is lost.
- **Fix risk:** Low.

#### M6. No brute-force protection on login, and login leaks account state
- **Status:** 🟡 Partly fixed in `d3b6511`. The password is checked before account state, and failures count toward Identity lockout (5 attempts, 15 minutes, `Auth.AccountLockedOut` → 401). Covered by `AuthSecurityTests`. Still open: request rate limiting.
- **Where:**
  - `AuthService.cs:229` uses `CheckPasswordAsync`, which doesn't count failures toward Identity lockout. There is no rate limiter in `Program.cs`.
  - `AuthService.cs:219-228` returns "account not active" or "email not confirmed" **before** the password check.
- **Impact:** unlimited password guessing. An attacker can also learn whether an email belongs to an unconfirmed or suspended account without knowing its password.
- **Fix risk:** Low–Medium. Use `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)` or ASP.NET rate limiting, and check the password first.

#### M7. Client IP is taken from a spoofable header
- **Status:** ✅ Fixed in `d3b6511`. The client IP comes from `RemoteIpAddress`. `UseForwardedHeaders` trusts loopback only; add the proxy to `KnownProxies` when deploying behind one.
- **Where:** `Presentation/Extensions/HttpContextExtensions.cs:9-11` trusts `X-Forwarded-For` from any client.
- **Impact:** the IPs recorded on refresh tokens, and the (currently dead) theft-detection logic, can be forged.
- **Fix risk:** Low. Use `UseForwardedHeaders` with `KnownProxies`/`KnownNetworks` and read `Connection.RemoteIpAddress`.

#### M8. Raw refresh tokens are logged on failure
- **Status:** ✅ Fixed in `d3b6511`. The catch-all is removed (see NM1), so the raw token is no longer logged.
- **Where:** `AuthService.cs:275` logs `{RefreshToken}`, and puts `ex.Message` in the template instead of passing the exception.
- **Impact:** tokens in logs. Most failed tokens are already invalid, but concurrency or suspicious-activity failures can involve live ones.
- **Fix risk:** Low.

#### M9. Reminder job can lose reminders and scales badly
- **Status:** ✅ Fixed in `e5cc0db`. The email is sent first, and `ReminderSentAt` is saved together with the notification. Candidates are filtered by date in SQL and loaded asynchronously. A lock is still needed if the app ever runs on more than one instance.
- **Where:**
  - `ClinicFlow/BackgroundJobs/AppointmentReminderJob.cs:58-74`.
  - `NotificationService.cs:13` (its `SaveChangesAsync` flushes the job's tracked changes).
  - `AppointmentRepo.cs:95-114`.
- **Lost emails:** `ReminderSentAt` is set *before* the email is sent. The next iteration's `CreateAsync` (or the final `SaveChangesAsync`) persists it even if the email throws, so failed emails are never retried.
- **Memory:** the query pulls *every* confirmed, un-reminded appointment with four includes into memory (`AsEnumerable`) every hour. The method is `async` with no `await`.
- **Fix risk:** Low–Medium. Set `ReminderSentAt` after a successful send, filter by date in SQL, and add a lock if the app ever runs on more than one instance.

#### M10. User-controlled names are injected into HTML emails
- **Status:** ✅ Fixed in `d3b6511`. Names are passed through `HtmlEncoder.Default.Encode`.
- **Where:** `Services/Implementations/EmailService.cs:95`, `:122`, `:259`.
- **Impact:** `FirstName`/`LastName` can be set freely (`ProfileService.cs:48-49`) and are interpolated into the HTML unescaped. That allows links or markup in transactional emails, which is a phishing vector.
- **Fix risk:** Low. Wrap values with `HtmlEncoder.Default.Encode`.

#### M11. Frontend URL and CORS origin are hardcoded to `http://localhost:3000`
- **Status:** ✅ Fixed in `d3b6511`. `Frontend:BaseUrl` (default `http://localhost:3000`) drives both CORS and the email links.
- **Where:** `EmailService.cs:25`, `:56`; `Program.cs:46`.
- **Impact:** email confirmation and password reset links are broken in any deployed environment.
- **Fix risk:** Low. Move them to config.

#### M12. Docker healthcheck always fails
- **Status:** ✅ Fixed. `Program.cs` maps `/health` (`AddHealthChecks()`/`MapHealthChecks`, a liveness check that doesn't touch the database) and the runtime image installs `curl`. Compose also sets `restart: unless-stopped` on `db` and `backend`.
- **Where:** `Dockerfile:39-40` curls `/health`. No health endpoint is mapped, and `curl` isn't in the `aspnet` image.
- **Fix risk:** Low. Add `AddHealthChecks()`/`MapHealthChecks("/health")` and a probe that exists in the image.

#### M13. Missing or inconsistent input validation
- **Status:** ⏸ Open. New validators would turn requests that are accepted today into 400s (an API change).
- **No validator:**
  - `BookAppointmentRequest`: `ReasonForVisit` is unbounded.
  - `CancelAppointmentRequest`, `DoctorNotesRequest`, `LoginRequest`, `ResetPasswordRequest`, `AppointmentFilterRequest` (H6).
- **Weaker reset policy:** `ResetPasswordRequest` gets only Identity's policy (`InfraStructureServiceExtensions.cs:28-29`: 8 chars, no symbol required). That is weaker than the registration rules (`RegisterPatientRequestValidator.cs:26-32`).
- **`RegisterDoctorRequestValidator` gaps:** it doesn't check `SpecialtyId`, `YearsOfExperience`, `ConsultationFee` (can be negative) or `Bio` length (DB max 500). Bad input surfaces as a generic "registration failed" 500.
- **Inconsistent phone rules:** E.164 at registration (`RegisterPatientRequestValidator.cs:20`) vs `^\+?[0-9]{7,15}$` on update (`UpdatePatientProfileRequestValidator.cs:25`, `UpdateUserProfileRequestValidator.cs:19`).
- **Fix risk:** Low.

#### M14. Appointment state rules are incomplete
- **Status:** ⏸ Open. Needs the state-machine and product decisions (§4.3).
- **Early completion:** a doctor can mark an appointment `Completed` before it happens (`AppointmentService.cs:302-312`, no time check), which unlocks a review for a visit that never took place.
- **NoShow:** never set by any code path, but cancel and complete both accept it.
- **Wrong error:** `CancelByAdminAsync` returns `AlreadyCancelled` for a *completed* appointment (`:251-255`).
- **Fix risk:** Medium. Centralise allowed transitions (see §4).

#### M15. Doctor-profile update forces a fee
- **Status:** ⏸ Open. Changes the request contract (`decimal?`).
- **Where:** `Shared/DTOs/Doctor/UpdateDoctorProfileRequest.cs:6` (non-nullable `decimal`) and `UpdateDoctorProfileRequestValidator.cs:11-13`.
- **Impact:** a "partial" update without a fee is rejected (fee must be > 0). If the validator were relaxed, the fee would be overwritten with 0, because the null-check mapping condition never applies to value types.
- **Fix risk:** Low. Make it `decimal?`.

### Low

| ID | Problem | Where | Fix risk | Status |
|---|---|---|---|---|
| L1 | Booking email says "Dr. Dr. Ahmed": the service passes `"Dr. X"` and the template prepends "Dr." | `AppointmentService.cs:92`; `EmailService.cs:91`, `:259` | Low | ✅ `3a2c391` |
| L2 | Doctor-facing cancel message repeats "on {date} on {date}" | `AppointmentService.cs:175`, `:236`, `:275` | Low | ✅ `3a2c391` |
| L3 | Booking email says cancel "24 hours in advance"; the rule is 2 hours | `EmailService.cs:101-102` vs `AppointmentService.cs:147` | Low | ✅ `d3b6511` |
| L4 | Missing slot returns "Appointment with id X was not found" | `AppointmentService.cs:30` | Low | ✅ `3a2c391` (new `AppointmentErrors.SlotNotFound`, still 404) |
| L5 | Schedule `IsActive=false` still regenerates bookable slots | `DoctorService.cs:101-102`, `:143-173` | Low | ✅ `3a2c391` |
| L6 | Inactive specialties can't be read, updated or reactivated (`GetByIdAsync` filters `IsActive`, and `SpecialtyRequest` has no `IsActive`) | `SpecialtyRepo.cs:17-19`; `Shared/DTOs/Specialty/SpecialtyRequest.cs` | Low | ⏸ Open (request contract change) |
| L7 | Logout requires a valid access token (so you can't log out after 15 min) and doesn't check that the refresh token belongs to the caller | `AuthController.cs:67-77`; `TokenService.cs:135-150` | Low | 🟡 `d3b6511`: the token must belong to the caller; logout still requires a valid access token |
| L8 | Two concurrent review submissions → unique-index violation → 500. Rating recalculation is a separate, non-atomic save | `ReviewService.cs:21-54`, `:108-117` | Low | 🟡 `3a2c391`: a duplicate now returns `ReviewAlreadyExists` (409); the rating recalculation is still a separate save. Covered by `SubmitReviewAsyncTests` and `SubmitReviewTests` (5 concurrent submits → one 201, the rest 409) |
| L9 | `AuthErrors.UserNotFound` says "No account found with this email" but is used for user-id lookups | `AuthErrors.cs:28-29`; `AuthService.cs:193`, `:308` | Low | ✅ `d3b6511` |
| L10 | Seeder comment says appointment dates are "pinned to July 2026"; they are relative to today | `DataSeeder.cs:566` | Low | ✅ `e5cc0db` |

### Duplicated logic

| What | Where | Fix risk |
|---|---|---|
| Three near-identical cancel flows (lookup, ownership, status checks, mutation, two notifications) | `AppointmentService.cs:114-281` | Medium (behaviour differs slightly per role; needs tests) |
| Doctor and patient registration share ~70 lines (transaction, user creation, role, email) | `AuthService.cs:17-104` vs `:106-183` | Low |
| Doctor reviews are served by two services and two routes | `DoctorService.cs:121-127` + `DoctorController.cs:50-58` vs `ReviewService.cs:89-104` + `ReviewController.cs:41-49` | Low |
| Specialty CRUD is exposed twice (one copy unprotected, see C1) | `SpecialtyController.cs` vs `AdminController.cs:52-87` | Low |
| `Review → ReviewResponse` mapping is defined in two profiles | `MappingProfiles/DoctorProfile.cs:28-29`, `MappingProfiles/ReviewProfile.cs:9-11` | Low |
| Slot generation exists twice with different loop conditions (the seeder's is correct) | `DoctorService.cs:143-173` vs `DataSeeder.cs:518-557` | Low |
| Paging (`Skip`/`Take`) is written in three repos with inconsistent clamping | `AppointmentRepo.cs:41-48`, `:71-76`; `DoctorRepo.cs:35-38`; `ReviewRepo.cs:28-31` | Low |
| Password and phone rules are copied between validators (phone rules differ; see M13) | `Presentation/Validators/Register*Validator.cs`, `Update*ProfileRequestValidator.cs` | Low |
| `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` is repeated in ~20 actions | All controllers | Low |

**Status:** the `Guid.Parse(...)` row is ✅ fixed in `d3b6511` (`User.GetUserId()` in `Presentation/Extensions/HttpContextExtensions.cs`). The other rows are ⏸ open.

### Dead code

| What | Where | Fix risk |
|---|---|---|
| Unused repository methods: `DoctorRepo.GetDetailedByIdAsync`, `ExistsAsync`, `Update`, `UpdateSchedule`; `AppointmentRepo.GetDetailedByIdAsync`, `HasCompletedAppointmentAsync`, `Update`; `SlotRepo.IsAvailableAsync`, `Block`, `Update`; `PatientRepo.GetAllPatientsAsync`, `Update`, `Delete` | `Persistance/Repositories/*` and the matching interfaces | Low |
| Unused errors: `DoctorErrors.NotApproved`, `CannotDeleteScheduleWithBookedSlots`, `InvalidScheduleTime`, `InvalidConsultationFee`; `ReviewErrors.AlreadyExists`, `NotAllowed`; `AppointmentErrors.CannotCancelCompleted`, `InvalidStatusTransition`; `AdminErrors.DoctorNotFound`; `Error.None` | `Shared/Errors/DoctorErrors.cs:10,22,25,28`; `ReviewErrors.cs:10,13`; `AppointmentErrors.cs:31,47`; `Error.cs:9` | Low |
| Unreachable checks in token refresh | `TokenService.cs:63-78` (see H7) | Medium |
| `existing is null` can never be true (`ToListAsync` never returns null), so `ScheduleNotFoundForDate` is unreachable | `DoctorService.cs:131-135` | Low |
| `AppointmentStatus.Pending` and `NoShow` are never set by application code. `Review.IsVisible` is never set to false. `Specialty.IsActive` can't be changed via the API | `Domain/Enums/AppointmentStatus.cs`; `Review.cs:14`; `Specialty.cs:8` | Medium (decide: implement or remove; removing enum values affects stored strings) |
| Empty `#region Helpers` | `AppointmentService.cs:395-400` | Low |
| Empty placeholder folders declared in csproj files | `Domain.csproj:10`, `Persistence.csproj:23`, `Presentation.csproj:16-17`, `Shared.csproj:10` | Low |
| Unused usings, including the internal EF API `Microsoft.EntityFrameworkCore.Query.Internal` and `Microsoft.AspNetCore.Rewrite` | `AuthService.cs:3-5`; `ScheduleResponse.cs:1`; `Review.cs:1`; `DoctorRepo.cs:2`; `ReviewController.cs:4,5,7` | Low |
| Unused field in a test (build warning CS0169) | `CancelAppointmentByPatientTests.cs:21` | Low |

**Status:** ✅ fixed:
- the unreachable refresh checks (removed with H7, `d3b6511`)
- the unused usings in `AuthService.cs` (`d3b6511`)
- the unused test field (`9035a7d`)

`DoctorErrors.InvalidScheduleTime` is now used (H5). `IRefreshTokenRepo.GetActiveByUserIdAsync` was replaced by `RevokeAllActiveAsync` (`d3b6511`). The other rows are ⏸ open.

### Unclear naming and misplaced types

| What | Where |
|---|---|
| Folder `Persistance` vs project and namespaces `Persistence`; files named `Global.cs.cs` | `Persistance/`, `Persistance/Global.cs.cs`, `Services.Abstraction/Global.cs.cs` |
| Enum namespaces don't match their location: `Domain.Enums`, `ClinicFlow.Domain.Enums` and `Shared.Enums` all live in `Domain/Enums` | `Domain/Enums/*.cs` |
| AutoMapper profiles are named `DoctorProfile`/`PatientProfile`, the same as entities, forcing fully-qualified names | `Services/MappingProfiles/DoctorProfile.cs`, `PatientProfile.cs`; `ReviewService.cs:108` |
| `DoctorNotesRequest` is in namespace `Shared.Errors`, with a lowercase `notes` property | `Shared/DTOs/Appointment/DoctorNotesRequest.cs:1,4` |
| `NotificationService` and `ServiceAssemblyReference` are in the global namespace | `NotificationService.cs:4`; `ServiceAssemblyReference.cs:1` |
| Base controller `ApiController` has the same name as the `[ApiController]` attribute, and is non-abstract (so it registers as an empty controller) | `Presentation/Controllers/ApiController.cs:5` |
| `AppointmentErrors.Unauthorized` and `ReviewErrors.Unauthorized` are 403 Forbidden errors | `AppointmentErrors.cs:39-41`; `ReviewErrors.cs` |
| `GetPatientsAppointmentsAsync`/`GetDoctorsAppointmentsAsync` take a *user* id, not a profile id | `IAppointmentRepo.cs:10-11` |
| PascalCase locals: `SlotUpdateResult`, `RefreshTokenExpirationDays`, `ATexpiresAt`, `Today`, `DayOfSet` (means "day offset") | `AppointmentService.cs:46`; `TokenService.cs:33,42,99`; `DoctorService.cs:146,148` |
| `InfraStructureServiceExtensions` (odd casing) | `ClinicFlow/Extensions/InfraStructureServiceExtensions.cs` |
| Misleading comments: header paths point to the wrong folder; "used task.fromresult" where none is used | `HttpContextExtensions.cs:1`; `CreateScheduleRequestValidator.cs:1`; `AuthService.cs:262` |

### Missing tests

What exists: unit tests for `AppointmentService.BookAppointmentAsync` and `CancelByPatientAsync` (15 test methods), plus 17 integration tests covering booking, patient cancel, patient list, login and refresh.

**Untested**
- **Services:** `AuthService` (registration, confirm, forgot/reset password, logout), `TokenService` (rotation, reuse, security stamp, concurrency), `DoctorService` (schedule create/update/delete, slot generation: H1, H4, H5, H8), `ReviewService` (H2), `AdminService`, `SpecialtyService`, `NotificationService`, `ProfileService`, `PatientService`.
- **Appointment flows:** cancel by doctor/admin, complete, doctor notes, `GetAppointmentAsync` (C4).
- **Infrastructure:** `AppointmentReminderJob`, `ValidationFilter`, `ApiController` status mapping, every validator.
- **Authorization:** no tests at all for C1 or C4.

**Existing tests that are wrong or misleading**
- `CancelAppointment_AlreadyCancelled_Returns403` (`CancelAppointmentByPatientTests.cs:72-86`) never tests "already cancelled". The cancelled appointment is seeded for a *different* patient (suffix `"2"`, `SeedDataHelper.cs:74`), so the request fails the ownership check (403) first. The real already-cancelled path returns 409 and is untested at the API level.
- `CancelAppointment_ValidRequest_Returns200` asserts 204 (`:55-69`).
- `CancelAppointment_WrongRole_Returns403` posts to the hardcoded appointment id `1` (`:120`).
- Typos in test names: `CancelAppoitment_*`, `WhenWhenAllInputsAreValid`, `WhenCancellsWithIn2Hours`.

**Fix risk:** Low. Adding tests can't break production. Write tests for H4/H6/H7 *before* fixing them.

**Status:** 🟡 Partly addressed. Added unit tests: `GetAppointmentAsyncTests` (C4), `DeleteReviewAsyncTests` (H2), `CreateScheduleAsyncTests` (H1) and `UpdateScheduleAsyncTests` (H5, L5, using the real AutoMapper configuration). Added integration tests: `AuthorizationTests` (C1, C4), `AuthSecurityTests` (H3, H7, M6, L7) and a names assertion in `GetPatientAppointmentsTests` (NM4). The three wrong cancel tests are ✅ corrected in `9035a7d`: already-cancelled now asserts 409, a cross-patient 403 test was added, the names were fixed, and the hardcoded id was replaced. Every `AppointmentService` method now has a unit test class: `CancelByDoctorAsyncTests`, `CancelByAdminAsyncTests`, `CompleteAppointmentAsyncTests`, `AddDoctorNotesAsyncTests`, `GetDoctorAppointmentsAsyncTests` and `GetPatientAppointmentsAsyncTests` (34 tests, branch `test/appointment-service-tests`). They cover ownership, each status error, the freed slot, notifications, and notification failures not failing the operation. `CancelByAdminAsyncTests` pins the M14 "wrong error" behaviour as documented in the contract; early completion (M14) is not tested until the rule is decided. The doctor and admin paths of `POST /api/appointments/{id}/cancel`, complete and notes still have no integration tests. `ReviewService` is now fully covered: `SubmitReviewAsyncTests` and `GetDoctorReviewsAsyncTests` (unit, using the real AutoMapper configuration) and `SubmitReviewTests` (integration: rating update, a duplicate and concurrent duplicates against the unique index, ownership, status, role and rating range). `AdminService` is now fully covered: unit tests for every method, plus `AdminDoctorTests` (integration). These show that suspending through the endpoint revokes the doctor's refresh tokens (H3) so refresh and login fail. They also cover approve and the pending list, unknown ids, non-admin callers, and the documented M3 500s. `AuthService` is now fully covered (branch `test/auth-service-tests`): unit tests for every method (41 tests) cover registration (transaction rollback on each failure, a failed confirmation email not failing registration), confirm email, login ordering (lockout before the password check, account state only revealed after a correct password), forgot and reset password (no email for unknown or suspended accounts, sessions revoked and the security stamp rotated). `RegistrationTests` and `PasswordResetTests` (integration) run the flows end to end with the token taken from the recorded email. They also show that a doctor registration whose profile insert fails rolls back the Identity user and role. Integration tests now replace `IEmailService` with a recording `FakeEmailService`; before this, they ran with the SMTP credentials in `appsettings.Development.json` and tried to send real email. `NotificationService`, the reminder job and the validators are still untested.

---

## 4. What will make new features harder

1. **No time-zone model, and "now" is read in about 28 places.** `IDateTimeProvider` exists, but `DateTime.UtcNow` is still used directly in services, repositories and entities (e.g. `RefreshToken.IsExpired`, `Appointment.BookedAt`, `SlotRepo.cs:37`, `AppointmentRepo.cs:97`). Any feature involving dates (reschedule, reminders at a chosen time, multi-clinic) will first need M2 solved and time made injectable everywhere.
2. **List endpoints return bare arrays and throw away `totalCount`** (`AppointmentService.cs:378`, `:391`; `DoctorService.cs:16-24`; `ReviewService.cs:102`). Adding real pagination to the UI means changing every list response shape, which is a breaking API change. Introduce a `PagedResponse<T>` before more list endpoints are added.
3. **No appointment state machine.** Status checks are copied into every method (cancel ×3, complete, notes, review), and two enum values are never used. Adding reschedule, no-show handling or "pending confirmation" means editing every copy. A method on `Appointment`, such as `Cancel(by, now)` or `Complete(now)` returning `Result`, would centralise the rules.
4. **Notifications and emails are hand-called inside each service method.** There are TODOs for domain events at `AppointmentService.cs:225` and `:264`. Every new channel (SMS, push) or new event means editing every service and repeating the try/catch dance (M4). Domain events or an outbox would decouple this.
5. **Slot materialisation is fragile.** A 14-day window with no roll-forward (H8), regeneration that collides with booked slots (H4), and only one schedule per weekday (`DoctorService.cs:69-75`; yet the seeder creates two per day: split shifts). Features like holidays, blocking individual slots or variable slot lengths will fight this design. Settle on idempotent "ensure slots exist until date X" generation first.
6. **Layering leaks.** `Domain` depends on EF Core, `Services` references `Persistence`, `Shared` references `Domain`, and an interface sits in the wrong namespace (§1.1). The project boundaries don't stop shortcuts, so they will keep accumulating. Moving `IDbContextTransaction` behind a domain abstraction and dropping `Services → Persistence` would let the compiler enforce the architecture.
7. **Hardcoded strings and config.** Role names appear as literals in 24 places across controllers and services. The frontend URL and CORS origin are hardcoded (M11). Adding a role (e.g. receptionist) or a second environment means a search-and-replace. Use constants and an options class.
8. **The error-type convention is being misused** (M3: `Failure` for conflicts and not-found cases). New code copies existing errors, so the wrong status codes spread. Fix the catalog and document "Failure = unexpected server error only".
9. **Startup does migrations and seeding** (`Program.cs:55`). This blocks running more than one instance, makes production deploys risky (C2), and slows down integration tests (each test class drops and re-migrates the DB, `ClinicFlowApiFactory.cs:81-87`). Move migrations to a deploy step and gate seeding.
10. **Little test coverage outside two `AppointmentService` methods, and no CI** (`.github/workflows` is empty). Any refactor in §3 (cancel deduplication, schedule rework, token family revocation) currently happens without a safety net.

---

## Suggested order of work
1. **Same day, all low fix risk:** C1, C3, C4, M8, H2, H3, H1.
2. **Gate seeding (C2)**, then add a global exception handler (M5) and paging defaults and validation (H6, M13).
3. **Fix error types (M3)** together with the frontend.
4. **Rework schedules and slots (H4, H5, H8, L5)** behind new tests.
5. **Time-zone model (M2)** and an appointment state machine (§4.3).
6. **Refresh-token family revocation (H7)** with integration tests.
7. **README corrections (§2).**

---

## .NET-specific findings

Second pass, same branch and commit (`876bbde`), 2026-10-09, read-only. It covers async usage, dependency injection, EF Core data access, the API layer, nullable reference types, secrets, disposal, build warnings and NuGet packages. Items already in §3 are not repeated: C1 (missing `[Authorize]`), M5 (no exception handler), M8 (token logging), M9 (reminder job loads everything synchronously), M13 (missing validators), H6 (appointment paging), §4.1 (`DateTime.UtcNow`).

### Checked and clean
- **Blocking async:** there is no `.Result`, `.Wait()`, `GetAwaiter().GetResult()` or `async void` anywhere in the solution.
- **HttpClient:** none is created manually, and there are no outbound HTTP calls.
- **DI lifetimes:** services, `DbContext` and `IUnitOfWork` are scoped. `DateTimeProvider` is a stateless singleton. `AppointmentReminderJob` creates a new scope on each run. No singleton captures a scoped service.
- **Service locator:** it appears in three places, and each one is legitimate. `ValidationFilter` uses it for the open-generic `IValidator<T>` lookup, the hosted job uses it for scope creation, and `DataSeeder` uses it at startup.
- **Lazy loading:** no lazy-loading proxies are used, so there are no hidden N+1 queries through navigation properties.
- **Nullable:** `<Nullable>enable</Nullable>` is set in all 9 projects. There is no `#nullable disable`, `SuppressMessage` or `#pragma warning disable` outside generated migrations. The only `NoWarn` is CS1591 (missing XML docs, `Presentation.csproj`). The build produces **no nullable (CS86xx) warnings**.
- **Secrets:** none are committed.
  - `appsettings.json` contains no connection string, JWT secret or SMTP password.
  - `appsettings.Development.json` and `.env` are git-ignored and never appear in `git log --all`.
  - `UserSecretsId` is set (`ClinicFlow.csproj:7`).
  - `docker-compose.yml` takes secrets from `${...}` variables.
  - The Testcontainers password in `ClinicFlowApiFactory.cs:17` is test-only.

### Build and package results

**`dotnet build ClinicFlow.slnx --no-incremental`:** 0 errors, 13 warnings (deduplicated below).

| Warning | Where | Notes |
|---|---|---|
| NU1903 ×4 | `ClinicFlow`, `ClinicFlow.IntegrationTests` | Vulnerable transitive packages: `Microsoft.OpenApi` (reported once in each of the two projects) and `SSH.NET` (one per advisory). See NH1 and NL19 |
| CS0114 | `ClinicFlow.IntegrationTests/Infrastructure/ClinicFlowApiFactory.cs:25` | `DisposeAsync()` hides `WebApplicationFactory<Program>.DisposeAsync()`. See NL20 |
| CS0618 | `ClinicFlowApiFactory.cs:15` | The parameterless `MsSqlBuilder()` is obsolete. Pass the image to the constructor |
| CS8981 ×2 | `Persistance/AppData/Migrations/20260704194735_appointment status is string in the db.cs:8` and `.Designer.cs:16` | The migration class name is all lower-case (`appointmentstatusisstringinthedb`) because the migration name had spaces. This is harmless, but use PascalCase names for future migrations |
| CS0169 | `CancelAppointmentByPatientTests.cs:21` | Already listed under Dead code |

The table lists 9 distinct warnings. The build summary reports 13 because some NU1903 warnings are repeated across restore and build. There are no nullable or analyzer warnings.

**Status:** ✅ after `9035a7d`, `dotnet build --no-incremental` reports 0 warnings.

**`dotnet list package --vulnerable --include-transitive`**

| Project | Package (transitive) | Resolved | Severity | Advisory | Pulled in by |
|---|---|---|---|---|---|
| `ClinicFlow` (and `IntegrationTests` through it) | `Microsoft.OpenApi` | 2.0.0 | High | GHSA-v5pm-xwqc-g5wc | `Microsoft.AspNetCore.OpenApi` 10.0.7 |
| `ClinicFlow.IntegrationTests` | `SSH.NET` | 2025.1.0 | High | GHSA-q939-rpr3-3284, GHSA-mggc-4xg6-vcxf | `Testcontainers.MsSql` 4.13.0 → `Testcontainers` |

**`dotnet list package --outdated`** (top-level packages)

| Project | Package | Current | Latest |
|---|---|---|---|
| `Persistence` | `Microsoft.EntityFrameworkCore`, `.Design`, `.SqlServer` | 10.0.7 | 10.0.12 |
| `Domain` | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.7 | 10.0.12 |
| `Services` | `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.8 | 10.0.12 |
| `Services` | `AutoMapper` | 16.1.1 | 16.2.0 |
| `Services` | `MailKit` | 4.17.0 | 4.18.1 |
| `ClinicFlow` | `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.Design`, `.Tools` | 10.0.7 | 10.0.12 |
| `ClinicFlow` | `Scalar.AspNetCore` | 2.14.14 | 2.17.14 |
| `ClinicFlow` | `Serilog` | 4.3.1 | 4.4.0 |
| `ClinicFlow` | `Serilog.Sinks.Seq` | 9.0.0 | 9.1.0 |
| `ClinicFlow.IntegrationTests` | `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 | 10.0.12 |
| `ClinicFlow.IntegrationTests` | `Testcontainers.MsSql` | 4.13.0 | 4.16.0 |
| `ClinicFlow.IntegrationTests` | `FluentAssertions` | 8.10.0 | 8.11.0 |
| both test projects | `Microsoft.NET.Test.Sdk` | 17.14.1 | 18.10.1 (major) |
| both test projects | `xunit.runner.visualstudio` | 3.1.4 | 4.0.0 (major) |
| both test projects | `coverlet.collector` | 6.0.4 | 10.1.0 (major) |
| `ClinicFlow.Tests` | `NSubstitute` | 6.0.0 | 6.2.0 |

The `Microsoft.*` 10.0.x packages are pinned at three different patch levels: 10.0.7, 10.0.8 and 10.0.10. There is no `Directory.Packages.props` (Central Package Management) to keep them aligned.

**Status:** ✅ all `Microsoft.*` 10.0.x packages are on 10.0.12 and `Testcontainers.MsSql` is on 4.16.0 (`9035a7d`). Central Package Management was not added. The other outdated packages (AutoMapper, MailKit, Serilog, Scalar and the test tooling majors) were not updated.

**New advisory (2026-10-16):** `dotnet list package --vulnerable --include-transitive` again reports a vulnerable package, so the "0 warnings" above no longer holds. The transitive `System.Security.Cryptography.Pkcs` is flagged in every project except `Domain`, `Shared`, `Services.Abstraction` and `Presentation`: 10.0.0 in most of them, 9.0.11 in `Persistence`. It has GHSA-846f-f764-76mx (High, NU1903) and GHSA-2fmv-rv2c-59x7 (moderate, NU1902). Status: ✅ Fixed in `4c3deaa`. It comes in through `Microsoft.Data.SqlClient` (from `Microsoft.EntityFrameworkCore.SqlServer`) and `MimeKit` (from MailKit). Both advisories are fixed in 10.0.13, but even after updating every `Microsoft.*` package to 10.0.13, SqlClient 6.1.7 and MimeKit 4.17 still resolve vulnerable versions. `Persistence.csproj` now pins `System.Security.Cryptography.Pkcs` 10.0.13, and every project that used it resolves that version through its reference to Persistence. Remove the pin once both depend on a patched version. `dotnet list package --vulnerable --include-transitive` is clean again, and the build has 0 warnings.

### High

#### NH1. Vulnerable `Microsoft.OpenApi` 2.0.0 ships in the production app
- **Status:** ✅ Fixed in `9035a7d`. The `Microsoft.*` packages are aligned on 10.0.12, and `dotnet list package --vulnerable --include-transitive` reports none.
- **Where:** `ClinicFlow/ClinicFlow.csproj:12` (`Microsoft.AspNetCore.OpenApi` 10.0.7) pulls in `Microsoft.OpenApi` 2.0.0 (GHSA-v5pm-xwqc-g5wc, High).
- **Impact:** the package is published into the Docker image. The OpenAPI document and Scalar are mapped only in Development (`Program.cs:71-75`), which limits how it can be reached in Production. It is still a known-High dependency in a deployable artifact, and security scanners will flag the image.
- **Fix risk:** Low. Update `Microsoft.AspNetCore.OpenApi` to 10.0.12 and re-run `dotnet list package --vulnerable`. If that doesn't resolve a patched `Microsoft.OpenApi`, add a direct `PackageReference` to the patched version.

### Medium

#### NM1. Token refresh and logout use exceptions for control flow, and a catch-all turns infrastructure failures into 401
- **Status:** ✅ Fixed in `d3b6511`. `TokenService` returns `Result`; `AuthService` has no catch-all, so real failures surface as 5xx.
- **Where:**
  - `Services/Implementations/TokenService.cs:57-78`, `:82`, `:86`, `:119` throw `SecurityTokenException`.
  - `:141` throws `KeyNotFoundException`.
  - `AuthService.cs:259-263` (logout) and `:273-277` (refresh) catch `Exception` and return `InvalidRefreshToken` (401).
- **Impact:** a database timeout, a deadlock or a cancelled request during refresh returns **401**. The frontend then treats the session as invalid and logs the user out. Genuine server faults never show up as 5xx, so monitoring misses them. This is also the only service that bypasses the `Result` convention described in CLAUDE.md.
- **Fix risk:** Low–Medium. Have `TokenService` return `Result<T>` with `AuthErrors`, and let unexpected exceptions propagate (to the handler from M5). Integration tests for refresh already exist (`RefreshTokenTests`).

#### NM2. `CancellationToken` isn't passed through auth, transactions or email
- **Status:** ✅ Fixed in `d3b6511`, `3a2c391` and `e5cc0db`. Two deliberate exceptions: post-commit emails in request paths don't get the request's `ct` (a client disconnect must not cancel the confirmation for a committed booking), and `ProfileService` has none because `UserManager` has no `ct` overloads.
- **Where:**
  - `ITokenService`/`TokenService`: no method takes a `ct`, so `SaveChangesAsync()` at `:40`, `:113` and `:148` runs without one.
  - `IRefreshTokenRepo`/`RefreshTokenRepo.cs:5-16`: none of the three methods takes a `ct`.
  - `IUnitOfWork.BeginTransactionAsync()` (`Persistance/UnitOfWork.cs:49-50`).
  - `CommitAsync()`/`RollbackAsync()` without a `ct`: `AppointmentService.cs:64`, `:108`; `TokenService.cs:114`, `:118`; `AuthService.cs:48`, `:58`, `:100`, `:138`, `:147`, `:180`.
  - `IEmailService` and every MailKit call in `EmailService.cs:299-309`.
  - `ProfileService` accepts `ct` but never uses it (`UserManager` has no `ct` overloads; this one is acceptable).
- **Impact:** when a client disconnects or the host shuts down, these paths keep their DB connection or SMTP session until they finish. Login, refresh and every email-sending request are the paths affected.
- **Fix risk:** Low. The change is mechanical and signature-only. NSubstitute setups in `ClinicFlow.Tests` need `Arg.Any<CancellationToken>()` added.

#### NM3. Email is sent inside the request, with no timeout and a new SMTP connection per message
- **Status:** 🟡 Partly fixed in `d3b6511`. Added a 30 s SMTP timeout and cancellation. Still open: sending is still inline (no background queue or outbox).
- **Where:** `EmailService.cs:297-309` creates a new `SmtpClient`, then connects, authenticates, sends and disconnects for every email. MailKit's default timeout is 2 minutes **per operation**, and no `ct` is passed. The callers are:
  - booking (`AppointmentService.cs:89`)
  - registration (`AuthService.cs:88`, `:169`)
  - forgot-password (`AuthService.cs:293`)
  - one call per appointment in `AppointmentReminderJob.cs:69`
- **Impact:** if the SMTP server is slow or unreachable, booking, registration and password reset hang for up to several minutes before the failure is swallowed or turned into an error (M4). Each hanging request holds a thread-pool continuation and, for booking, a tracked `DbContext`.
- **Fix risk:** Low–Medium. Set `client.Timeout` and pass `ct` now. Later, move sending to a queued background sender (`Channel<T>` + `BackgroundService`, or an outbox), which also covers M4 and §4.4.

#### NM4. Missing `Include`s make the appointment lists return blank names
- **Status:** ✅ Fixed in `3a2c391`. Covered by an assertion in `GetPatientAppointmentsTests`.
- **Where:** `Persistance/Repositories/AppointmentRepo.cs:29-35` includes `Doctor` but not `Doctor.User`, and `:57-65` doesn't include `Patient.User`. `Services/MappingProfiles/AppointmentProfile.cs:11-12` builds `DoctorName` and `PatientName` from those navigations.
- **Impact:** `GET /api/appointments/doctor/my` returns an empty `DoctorName`, and `GET /api/appointments/my` returns an empty `PatientName`. There is no error, because AutoMapper null-guards the expression and the field is silently blank.
- **Fix risk:** Low. Add `.ThenInclude(d => d.User)` and `.Include(x => x.Patient).ThenInclude(p => p.User)`, or better, use `ProjectTo<AppointmentResponse>` so EF selects only the needed columns.

#### NM5. No authorization fallback policy, so endpoints are public by default
- **Status:** ⏸ Open. A fallback policy changes which endpoints are anonymous (an API change).
- **Where:** `ClinicFlow/Extensions/InfraStructureServiceExtensions.cs` registers authentication, but there is no `AddAuthorization(o => o.FallbackPolicy = ...)`. Only `AdminController`, `NotificationController` and `ProfileController` have class-level `[Authorize]`.
- **Impact:** every action without an attribute is anonymous. C1 shows what happens: commenting out an attribute silently opens a write endpoint, and nothing fails. A new controller is public until someone remembers to protect it.
- **Fix risk:** Medium. Set `FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`, then add `[AllowAnonymous]` to the auth endpoints and to the public doctor, specialty and review reads. The integration tests cover login and refresh anonymously, so they will catch a missed `[AllowAnonymous]`.

#### NM6. Compose connects the app as `sa` and publishes SQL Server on every host interface
- **Status:** 🟡 Partly fixed in `e5cc0db`. The port is bound to `127.0.0.1`. Still open: the app connects as `sa`, because a least-privilege login needs a DB init script.
- **Where:** `docker-compose.yml:28` (`User Id=sa;Password=${DB_PASSWORD}`) and `:11` (`"1433:1433"`).
- **Impact:** an SQL injection or app compromise gets full server-admin rights. The database is reachable from the network with the SA password, and the compose file runs with `ASPNETCORE_ENVIRONMENT=Production`.
- **Fix risk:** Low. Create a least-privilege login for the app (it needs DDL only while startup migrations remain; see §4.9). Remove the port mapping, or bind it to `127.0.0.1:1433:1433`.

### Low

| ID | Problem | Where | Fix risk | Status |
|---|---|---|---|---|
| NL1 | Read-only queries are tracked (no `AsNoTracking`) on hot paths: public doctor search, doctor profile, pending approvals, schedules, available slots, specialties, `GetAppointmentAsync`, patient profile GET. That costs change-tracker work and memory per request | `DoctorRepo.cs:17-18`, `:25-29`, `:44-48`, `:57-60`; `SlotRepo.cs:14-19`; `SpecialtyRepo.cs:13-15`; `AppointmentRepo.cs:8-14`; `PatientRepo.cs:19` | Medium: `DoctorRepo.GetByIdAsync`, `AppointmentRepo.GetByIdAsync` and `PatientRepo.GetPatientByUserIdAsync` are shared with write paths, so split them into read and write methods | 🟡 `3a2c391`: pure-read queries only; methods shared with writes are not split yet |
| NL2 | Unbounded result sets: every notification a user has ever had is returned on each call, and the pending-doctor list has no paging | `NotificationRepo.cs:7-13`; `DoctorRepo.cs:42-51` | Low (adds paging, changes the API shape; see §4.2) | ⏸ Open (paging changes the API shape) |
| NL3 | The doctor search can't use an index: `ToLower().Contains(...)` on `FirstName + " " + LastName` and `ClinicCity` scans every row. The SQL Server default collation is already case-insensitive, so `ToLower` buys nothing | `DoctorRepo.cs:112-118` | Low | 🟡 `3a2c391`: `ToLower` removed; a leading-wildcard `LIKE` still scans (needs full-text search) |
| NL4 | Paging order isn't deterministic: the sort keys (`AverageRating`, fee, experience, `CreatedAt`) have ties and no unique tie-breaker, so rows can repeat or disappear between pages | `DoctorRepo.cs:120-126`; `ReviewRepo.cs:24` | Low (`.ThenBy(x => x.Id)`) | ✅ `3a2c391` |
| NL5 | The stats endpoints make 6 and 5 sequential `COUNT` round trips. One `GroupBy(a => a.Status)` query would cover the appointment stats | `AdminRepo.cs:7-12`; `AppointmentRepo.cs:117-121` | Low | 🟡 `3a2c391`: appointment stats run as one `GROUP BY`; the overview still runs 6 counts over different tables |
| NL6 | The reminder job saves one notification per `SaveChangesAsync`, plus one SMTP session per email: 2N round trips per run | `AppointmentReminderJob.cs:58-74` → `NotificationService.cs:11-13` | Low (use `CreateRangeAsync` and one SMTP connection per batch) | ⏸ Open: per-item save kept on purpose, so each sent reminder is recorded at once (no duplicate emails) |
| NL7 | Transactions are disposed synchronously (`using var` instead of `await using`). On early returns such as `SlotAlreadyBooked`, the implicit rollback blocks a thread-pool thread on I/O | `AppointmentService.cs:43`, `:50`; `TokenService.cs:53` | Low | ✅ `d3b6511`, `3a2c391` |
| NL8 | `ValidationFilter` is a synchronous `IActionFilter` that calls `Validate()`. Adding any `MustAsync` rule later will throw `AsyncValidatorInvokedSynchronouslyException` (500). When two arguments fail, the last result overwrites the first | `Presentation/Filters/ValidationFilter.cs:8`, `:30`, `:50` | Low (`IAsyncActionFilter` + `ValidateAsync(..., ct)`) | ✅ `e5cc0db` (response shape unchanged) |
| NL9 | 400 responses come in three shapes: the filter's bare `ValidationProblemDetails` (no `status`, `type` or `traceId`), `ValidationProblem()` from `ApiController`, and `[ApiController]`'s automatic 400 for binding errors. 401/403 from the auth middleware and 404 for unknown routes have empty bodies because `AddProblemDetails` isn't registered | `ValidationFilter.cs:50-51`; `ApiController.cs:86`; `Program.cs` | Low (`AddProblemDetails()` + `UseStatusCodePages()`, and build the filter's response with `ProblemDetailsFactory`) | ⏸ Open (API shape) |
| NL10 | The controller decides behaviour by role. It takes the *first* role claim and routes to one of three service methods, and `SpecialtyController` checks the role inline. Users with more than one role get whichever claim comes first | `AppointmentController.cs:94-103`; `SpecialtyController.cs:17` | Low (pass the `ClaimsPrincipal` or role to one service method, or use policies) | ⏸ Open (refactor only) |
| NL11 | `[FromQuery] DateOnly date` is non-nullable, so a missing `date` silently becomes `0001-01-01` and returns `[]` instead of 400 | `DoctorController.cs:42` | Low (`DateOnly?` + validation, or `[BindRequired]`) | ⏸ Open (missing `date` → 400 is an API change) |
| NL12 | Options are not validated at startup. `JwtSettings` is read eagerly with `Get<JwtSettings>()!` (a missing section causes a `NullReferenceException`) and then bound a second time. Neither `JwtSettings` nor `EmailSettings` uses `ValidateDataAnnotations().ValidateOnStart()`, so a secret shorter than 32 bytes only fails at the first login (IDX10720 → 500), and missing SMTP credentials only fail at the first send | `InfraStructureServiceExtensions.cs:36`, `:60-61`; `CoreServiceExtensions.cs:28` | Low | 🟡 `e5cc0db`: `JwtSettings` is validated on start; `EmailSettings` is not, because that would stop startup wherever email isn't configured |
| NL13 | A startup failure exits with code 0. The top-level `catch (Exception)` logs and swallows the error, so `restart: on-failure` and orchestrators see a clean exit. It also logs EF tooling's `HostAbortedException` as Fatal on every `dotnet ef` command | `ClinicFlow/Program.cs:85-91` | Low (filter `HostAbortedException`, `return 1`, use `await app.RunAsync()`) | ✅ `e5cc0db` |
| NL14 | No `EnableRetryOnFailure` on the SQL Server provider, so transient faults go straight to the user | `InfraStructureServiceExtensions.cs:21-24` | Medium (the manual `BeginTransactionAsync` calls must then run inside `Database.CreateExecutionStrategy().ExecuteAsync`) | ⏸ Open (Medium fix risk) |
| NL15 | EF SQL command logging is at Information in the **base** `appsettings.json`, so every SQL statement is written to file and Seq in Production. Parameter values are not logged, because `EnableSensitiveDataLogging` is off | `ClinicFlow/appsettings.json:7` | Low (move it to `appsettings.Development.json`) | ✅ `e5cc0db` |
| NL16 | `.env` isn't in `.dockerignore`, so `COPY . .` sends the JWT secret, SMTP password and DB password into the build context and the build-stage layers. The final runtime image is unaffected, because only `/app/publish` is copied | `.dockerignore`; `Dockerfile:19` | Low | ✅ `e5cc0db` |
| NL17 | Nullable hygiene. There are 20 `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` calls, which throw `ArgumentNullException` (500) if the claim is ever missing. `specialtyDTO.Name!` uses `!` on a property that is already non-nullable. The `Email!` uses assume Identity always has an email. Nullable warnings aren't treated as errors, so new ones can slip in | `Presentation/Controllers/*`; `SpecialtyService.cs:13`, `:78`; `AuthService.cs:95`, `:175`, `:245`; `AppointmentReminderJob.cs:70` | Low (a `User.GetUserId()` extension, and `<WarningsAsErrors>nullable</WarningsAsErrors>` in a `Directory.Build.props`) | 🟡 `d3b6511`, `e5cc0db`: `User.GetUserId()` added and nullable warnings are now errors (`Directory.Build.props`); the `Email!` and `Name!` uses remain |
| NL18 | `MimeMessage` (which is `IDisposable`) is never disposed. `JwtSecurityTokenHandler` is the legacy handler; `JsonWebTokenHandler` is the faster replacement that the JwtBearer middleware already uses | `EmailService.cs:291`; `TokenService.cs:179` | Low | 🟡 `d3b6511`: `MimeMessage` is disposed; `JwtSecurityTokenHandler` is kept, because switching changes the claim names in the token payload, which the frontend may read |
| NL19 | Vulnerable `SSH.NET` 2025.1.0 (2 High advisories) is pulled in by Testcontainers. It is test-only and never deployed, but it runs on developer machines | `ClinicFlow.IntegrationTests.csproj:16` | Low (update `Testcontainers.MsSql` to 4.16.0, then re-check) | ✅ `9035a7d` |
| NL20 | The test factory's `public async Task DisposeAsync()` (for `IAsyncLifetime`) hides `WebApplicationFactory.DisposeAsync()` (CS0114). It also only calls `StopAsync` on the container and never disposes it, so cleanup is left to the Testcontainers reaper | `ClinicFlowApiFactory.cs:25-28` | Low (implement `IAsyncLifetime.DisposeAsync` explicitly, call `await _dbContainer.DisposeAsync()` and `await base.DisposeAsync()`) | ✅ `9035a7d` |
| NL21 | `UnitOfWork` creates repositories itself (`new DoctorRepo(_context)`, …), so repositories can't be resolved, decorated (caching, logging) or replaced through DI. This is a deliberate pattern and works with the `IUnitOfWork` mocks in the tests; it is listed here only because the brief asked for classes that create their own dependencies | `Persistance/UnitOfWork.cs:26-47` | Medium (changes how every service gets its repositories; only worth doing if decoration is needed) | ⏸ Open (deliberate pattern) |
| NL22 | `SubmitReviewAsync` checks for an existing review before it checks ownership. A patient who submits a review for another patient's appointment gets 409 `Appointment.ReviewAlreadyExists` if that appointment has been reviewed, and 403 `Appointment.Unauthorized` if not. This reveals whether someone else's visit was reviewed. Found while writing `SubmitReviewAsyncTests` (2026-10-16) | `ReviewService.cs:21-32` | Low (move the duplicate check after the ownership and status checks; the status code changes for this edge case, so update `API_CONTRACT.md`) | ✅ `4c6d9fe` (contract 1.0.1). Ownership and status are now checked first. Covered by `SubmitReviewAsync_WhenAnotherPatientsAppointmentAlreadyReviewed_ReturnsUnauthorizedError` and `SubmitReview_OtherPatientsAlreadyReviewedAppointment_Returns403` |
| NL23 | The admin dashboard's `pendingApprovals` count includes suspended doctors, but `GET /api/admin/doctors/pending` excludes them (`d.User.IsActive`), so the count can be higher than the list. Found while writing `AdminDoctorTests` (2026-10-16) | `AdminRepo.cs:11` vs `DoctorRepo.cs:49` | Low (decide whether suspended applicants count as pending, then align the two queries) | ⏸ Open |
| NL24 | `ForgotPasswordAsync` sends the reset email inline and lets an SMTP failure propagate, so a registered, active email gets 500 while an unknown one gets 204. When SMTP is down or slow, the status code (and the response time, since only known emails reach SMTP) reveals which emails are registered, which the endpoint is meant to hide. Found while writing `ForgotPasswordAsyncTests` (2026-10-17) | `AuthService.cs:281` | Low (log and swallow the send failure and return 204, or queue the email as in NM3; the contract's "500 if the email cannot be sent" changes) | ✅ `165aa5e` (contract 1.0.2). The send failure is logged and the response is 204. Covered by `ForgotPasswordAsyncTests` and `ForgotPassword_EmailSendFails_StillReturns204LikeAnUnknownEmail`. Still open: the response **time** differs, because only known emails wait for SMTP; that goes away when sending moves off the request (NM3) |

### Suggested order for these items
1. **NH1 and NL19:** bump the packages (minutes of work). Update the `Microsoft.*` 10.0.x packages together, ideally after introducing `Directory.Packages.props`.
2. **NM4** and **NM5** (do NM5 together with C1).
3. **NM1 + NM2 + NM3** as one auth/email pass, ahead of the token-family work in H7.
4. **NM6, NL15, NL16:** deployment configuration, alongside C2.
