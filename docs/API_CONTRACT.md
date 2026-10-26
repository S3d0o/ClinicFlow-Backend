# ClinicFlow API Contract

| | |
|---|---|
| **Contract version** | 4.0.0-p0 (pre-release, `v2` branch). V1's contract stays 3.1.2 on `master` ([docs/v2/adr/0007](v2/adr/0007-v2-separate-product-line.md)) |
| **Last updated** | 2026-10-27 (§13 added; no API change) |
| **Applies to** | ClinicFlow backend (`ClinicFlow.Backend`) and the ClinicFlow frontend (React) |
| **Canonical copy** | `ClinicFlow.Backend/docs/API_CONTRACT.md`. The frontend repo keeps an identical copy at `docs/API_CONTRACT.md` |
| **Machine-readable companion** | [`openapi.json`](openapi.json) (OpenAPI 3.1, generated from the running API) |

This document is the single agreement between the two apps. It describes what the API **does today**, including known quirks, so the frontend can rely on it. Planned changes are listed separately in [§9](#9-pending-contract-changes) and become real only when they move into the changelog ([§10](#10-changelog)). The rules for changing the contract are in [§11](#11-how-to-change-this-contract).

The `/api/v2` endpoints the V2 frontend already builds against are agreed in advance in [§13](#13-planned-apiv2-endpoints-agreed-not-built). None of them exists yet; each moves into §4–§7 unchanged when it is built.

Status-code notes marked *(REVIEW Mx)* refer to known issues in [REVIEW.md](REVIEW.md) that are scheduled to change.

---

## Contents
1. [General conventions](#1-general-conventions)
2. [Authentication and sessions](#2-authentication-and-sessions)
3. [Errors](#3-errors)
4. [Endpoint index](#4-endpoint-index)
5. [Endpoints](#5-endpoints)
6. [Data types](#6-data-types)
7. [Enums](#7-enums)
8. [Frontend routes the backend depends on](#8-frontend-routes-the-backend-depends-on)
9. [Pending contract changes](#9-pending-contract-changes)
10. [Changelog](#10-changelog)
11. [How to change this contract](#11-how-to-change-this-contract)
12. [Appendix: TypeScript types](#12-appendix-typescript-types)
13. [Planned `/api/v2` endpoints (agreed, not built)](#13-planned-apiv2-endpoints-agreed-not-built)

---

## 1. General conventions

| Topic | Rule |
|---|---|
| Base URL | Development: `http://localhost:5143` or `https://localhost:7129`. Docker compose: `http://localhost:5001`. All paths start with `/api` |
| Versioning | No version segment in the URL. Breaking changes follow [§11](#11-how-to-change-this-contract) |
| Format | JSON (UTF-8). Send `Content-Type: application/json` on requests with a body |
| Property names | **camelCase** in both directions (`reasonForVisit`). Property names are matched case-insensitively on input |
| Enums | Sent as **strings** (`"Confirmed"`, `"Monday"`). Integers are accepted on input but **send strings** |
| `DateOnly` | `"yyyy-MM-dd"`, e.g. `"2026-10-11"` |
| `TimeOnly` | Responses use `"HH:mm:ss"` (`"09:30:00"`). Requests accept `"HH:mm"` or `"HH:mm:ss"` |
| Slot and schedule times | **Clinic local wall-clock time**, with no time zone. Show them as they are; do not convert. The clinic's zone is configured on the server (`Clinic:TimeZoneId`, default `Africa/Cairo`), and every time rule (slot in the past, 2-hour cancellation window, completion, no-show grace, reminders) uses the clinic's clock |
| Timestamps (`DateTime`) | UTC, always with the `Z` suffix (e.g. `"bookedAt": "2026-10-08T12:17:34.1027961Z"`), since 4.0.0-p0. Contract 3.x sent values read from the database without the `Z`; a client that appends a missing `Z` before parsing keeps working |
| Money | `consultationFee` is a JSON number (decimal, EGP), e.g. `500.00` |
| Ids | Entity ids are integers. User ids (`UserProfileResponse.id`, `confirm-email` / `reset-password` `userId`) are GUID strings |
| Nulls | Optional fields may be `null` or omitted in requests. Nullable response fields are always present, possibly as `null` |
| Lists | List endpoints return a **bare JSON array**, with no envelope. Notifications (#32) and pending doctors (#38) also send the total number of matching items in an **`X-Total-Count`** response header |
| Paging | `pageNumber` (1-based) and `pageSize` query parameters, where supported. See each endpoint for defaults and limits; an out-of-range value returns 400 (B) |
| Partial updates | `PATCH` endpoints, and `PUT /api/doctors/schedule/{id}`, apply only the fields you send. Omitted or `null` fields keep their current value |
| Route ids | A non-integer `{id}` returns 400 |
| CORS | Allowed origin: the configured frontend URL (default `http://localhost:3000`). Any header or method; credentials allowed. `X-Total-Count` and `Retry-After` are exposed to the browser (`Retry-After` since 3.1.2) |
| Authentication by default | Every endpoint requires a logged-in user unless §4 marks it anonymous (**—**). This includes unknown routes: an anonymous request to a route that doesn't exist gets 401, a logged-in one gets 404 |
| Notifications and emails | **Sent after the response, not during it** (since 4.0.0-p0). An action that notifies someone (booking, cancelling, completing, no-show, doctor approval or suspension, reminders) or sends an email (registration, forgot password, booking, reminders) succeeds first; the in-app notification and the email follow about 2 seconds later. A failed email is retried for about half an hour, and a sending problem never changes the action's response. Refresh the notifications list (#32) after a few seconds, or on the next visit, rather than expecting it the moment the action returns |
| Health probes | For deployments, not the frontend; anonymous, outside `/api` and the OpenAPI document (since 4.0.0-p0). `GET /health/live` (and `GET /health`, the 3.x route): `200` with the text `Healthy` while the process answers. `GET /health/ready`: JSON `{"status": "...", "checks": {"database": {"status": "..."}, "outbox": {"status": "...", "lagSeconds": n, "deadLettered": n}}}`; `200` when `Healthy` or `Degraded` (the oldest due notification or email has waited over a minute), `503` when `Unhealthy` (the database can't be reached) |

## 2. Authentication and sessions

### 2.1 Tokens

| Token | Lifetime | Where | Notes |
|---|---|---|---|
| Access token (JWT, HS256) | 15 min (`expiresAt` in the login response) | `Authorization: Bearer <accessToken>` | No clock skew: it is rejected at the exact expiry |
| Refresh token (opaque string) | 7 days | Request body of `refresh-token` / `logout` | **Single use.** Every refresh returns a new pair; the old refresh token stops working immediately |

`LoginResponse.role` is the user's role (`"Admin"`, `"Doctor"` or `"Patient"`). **Use `role` from the login/refresh response.** If you must decode the JWT, its claims are:

| Claim (exact key in the payload) | Value |
|---|---|
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | User id (GUID) |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | Full name |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | Role |
| `email`, `jti`, `exp`, `iss`, `aud` | Standard |

### 2.2 Login rules
- **Wrong password:** 401 `Auth.InvalidCredentials`. An unknown email returns the same error.
- **Lockout:** after **5** wrong passwords the account is locked for **15 minutes**. Every login attempt during that time, even with the correct password, returns 401 `Auth.AccountLockedOut`.
- **Account state is checked only after a correct password:**
  - suspended account → 401 `Auth.AccountNotActive`
  - email not confirmed → 401 `Auth.EmailNotConfirmed`
- **Unapproved doctors can log in** and set up their schedules. Until an admin approves them (and while a doctor is suspended) they are hidden from patients: no search result, a 404 profile, no slots, and their slots can't be booked.

### 2.3 Refresh rules (frontend MUST follow these)
1. **Refresh once at a time (single-flight).** When several requests get 401 at the same moment, make one refresh call and let the other requests wait for its result. Two parallel refreshes with the same token: one succeeds, the other gets 401.
2. **Never reuse an old refresh token.** Replace the stored token as soon as a refresh succeeds. Reusing an already-rotated token from a **different IP, or more than 5 minutes later**, is treated as theft and **revokes every session of that user** (all devices). Reuse from the same IP within 5 minutes is only rejected with 401.
3. **On 401 `Auth.InvalidRefreshToken`, the session is over:** clear the tokens and send the user to login. This happens when the token is expired, revoked, rotated, the account was suspended, or the password was reset.
4. **A 5xx from refresh is not a logout:** keep the tokens and retry later.

### 2.4 Detecting an expired access token
A protected endpoint called with a missing, invalid or expired access token returns **401 with a ProblemDetails body that has no error code** (shape E) and a `WWW-Authenticate: Bearer …` header (it contains `error="invalid_token"` when the token is expired or invalid). Refresh and retry once; if the retry also returns 401, log out.

### 2.5 Authorization failures
- A **wrong role** (e.g. a patient calling `/api/admin/*`) returns **403 with a ProblemDetails body that has no error code** (shape E).
- A **right role but not the owner** of the resource (e.g. another patient's appointment) returns **403 with a ProblemDetails body** whose `title` is the error code (e.g. `Appointment.Unauthorized`).

### 2.6 Logout
`POST /api/auth/logout` takes the refresh token in the body and revokes it. The access token is optional (since 3.1.0), so logout works after the 15-minute access token has expired, without refreshing first. If a valid access token is sent, the refresh token must belong to that user, or the response is 401. Whatever the response, clear the local tokens.

### 2.7 Rate limiting
Since 3.1.0, these endpoints share **one limit per client IP**: register patient (#1), register doctor (#2), login (#3), confirm email (#6), forgot password (#7) and reset password (#8). The limit is 10 requests per 60 seconds by default; the backend configures it (`RateLimiting:Auth`). It is a fixed window, and the endpoints count together, so 10 failed logins leave no attempts for a password reset in the same window.
- Over the limit: **429** ProblemDetails (shape A) with the code `RateLimit.Exceeded`, and a `Retry-After` header giving the seconds until the window resets (readable cross-origin since 3.1.2, because CORS exposes it). Show "Too many attempts, try again in N seconds" and don't retry automatically.
- Refresh (#4) and logout (#5) are **not** rate limited, so users behind one clinic IP don't lose their sessions.
- Account lockout ([§2.2](#22-login-rules)) applies independently, per account.

## 3. Errors

### 3.1 Response shapes

Every non-2xx response is one of these shapes. Branch on the **error code** wherever there is one, not only on the status: some codes will move to a different status (see [§9](#9-pending-contract-changes)).

**A. ProblemDetails (business errors).** `title` is the error code and `detail` is a human-readable message.
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Doctor.NotFound",
  "status": 404,
  "detail": "Doctor with id 9999 was not found",
  "traceId": "00-291e43…-00"
}
```

**B. Request validation (FluentValidation).** Status 400. Keys are the request property names in **PascalCase**; map them to your camelCase form fields.
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email must be a valid email address."],
    "Password": ["Password must be at least 8 characters long.", "Password must contain at least one digit."]
  },
  "traceId": "00-…-00"
}
```

**C. Business-rule validation.** Status 400. Same shape as B, but the key is the **error code**:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Appointment.CancellationWindowExpired": ["Appointments can only be cancelled at least 2 hours before the scheduled time"] },
  "traceId": "00-…-00"
}
```

**D. Malformed request (JSON or binding error).** Status 400, same shape as B. Keys are JSON paths such as `"$.email"`, the parameter name (e.g. `"date"` for a missing required query parameter), or `"request"`. This usually indicates a frontend bug.

B, C and D are one shape since 3.0.0: every 400 has `type`, `title`, `status`, `errors` and `traceId`.

**E. ProblemDetails without an error code.** Used for 401 from the auth middleware, 403 for a wrong role, 404 for an unknown route, and unhandled 500s. `title` is the standard reason phrase (e.g. `"Unauthorized"`), not an error code, so branch on `status`. A 500 carries no exception details outside Development.
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Unauthorized",
  "status": 401,
  "traceId": "00-…-00"
}
```

Every error body is JSON with `Content-Type: application/problem+json`; quote `traceId` when reporting a problem.

### 3.2 Status codes

| Status | Meaning in this API | Body |
|---|---|---|
| 200 | OK, with a body | Resource or array |
| 201 | Created, with a body (no `Location` header) | Created resource |
| 204 | Done, no body | — |
| 400 | Invalid input (B, C or D) | Validation shape |
| 401 | Not authenticated, bad credentials, or a session problem. Also an anonymous request to an unknown route | A (auth endpoints) or E (middleware) |
| 403 | Wrong role (E) or not the owner (A) | E or A |
| 404 | Resource not found (A), or unknown route for a logged-in user (E) | A or E |
| 409 | Conflict with the current state (A) | A |
| 429 | Too many requests to a rate-limited auth endpoint (since 3.1.0, [§2.7](#27-rate-limiting)) | A (`RateLimit.Exceeded`) |
| 500 | Unexpected server error only. Expected business errors use 400, 404 or 409 (since 2.0.0) | A or E |

### 3.3 Error code catalog

Only codes the API can actually return are listed.

| Code | Status | When |
|---|---|---|
| `Auth.InvalidCredentials` | 401 | Wrong email or password |
| `Auth.AccountLockedOut` | 401 | Too many failed logins; locked for 15 min |
| `Auth.AccountNotActive` | 401 | Account suspended |
| `Auth.EmailNotConfirmed` | 401 | Email not confirmed yet |
| `Auth.InvalidRefreshToken` | 401 | Refresh token invalid, expired, revoked or rotated, or the user is suspended; on logout with an access token, also when the token is not the caller's |
| `Auth.UserNotFound` | 404 | `userId` unknown (confirm email, reset password) |
| `Auth.EmailAlreadyExists` | 409 | Registration with an email that is already used |
| `Auth.RegistrationFailed` | 400 (shape C), or 500 | 400: Identity rejected the user (e.g. the password policy); the error lists the reasons. 500: an unexpected server failure while registering |
| `Auth.InvalidEmailConfirmationToken` | 400 (shape C) | Confirmation token invalid or expired |
| `Auth.InvalidPasswordResetToken` | 400 (shape C) | Reset token invalid or expired. (A weak new password is rejected first by validation, 400 B) |
| `Doctor.NotFound` | 404 | Doctor profile id unknown |
| `Doctor.ProfileNotFound` | 404 | The logged-in user has no doctor profile |
| `Doctor.ScheduleNotFound` | 404 | Schedule unknown or owned by another doctor |
| `Doctor.ScheduleDayAlreadyExists` | 409 | An active schedule for that weekday already exists |
| `Doctor.CannotDeleteScheduleWithBookedSlots` | 409 | Deleting a schedule while upcoming appointments are booked on it |
| `Doctor.InvalidScheduleTime` | 400 (shape C) | Schedule update would leave end ≤ start, or not fit one slot |
| `Patient.ProfileNotFound` | 404 | The logged-in user has no patient profile |
| `Appointment.NotFound` | 404 | Appointment id unknown |
| `Appointment.SlotNotFound` | 404 | Slot id unknown (booking) |
| `Appointment.SlotNotAvailable` | 409 | Slot is booked or blocked |
| `Appointment.SlotAlreadyBooked` | 409 | Someone else booked the slot first |
| `Appointment.SlotInPast` | 400 (shape C) | The slot has already started |
| `Appointment.CancellationWindowExpired` | 400 (shape C) | Patient cancelling less than 2 hours before the start |
| `Appointment.AlreadyCancelled` | 409 | Already cancelled |
| `Appointment.AlreadyCompleted` | 409 | Already completed |
| `Appointment.CannotCompleteCancelled` | 409 | Completing a cancelled appointment |
| `Appointment.CannotCompleteBeforeStart` | 400 (shape C) | Completing an appointment before its start time |
| `Appointment.AlreadyNoShow` | 409 | Cancelling, completing or marking again an appointment already marked as a no-show |
| `Appointment.NoShowTooEarly` | 400 (shape C) | Marking a no-show less than 15 minutes after the start time |
| `Appointment.DoctorNotesNotAllowed` | 409 | Notes on a cancelled (or rescheduled) appointment |
| `Appointment.InvalidStatusTransition` | 409 | A status change the appointment rules don't allow when none of the codes above applies, e.g. cancelling a visit that is in progress (since 4.0.0-p0). The message names both statuses |
| `Appointment.Unauthorized` | 403 | Not the owner of the appointment |
| `Appointment.ReviewAlreadyExists` | 409 | Appointment already reviewed |
| `Appointment.ReviewNotAllowed` | 400 (shape C) | Appointment not completed |
| `Review.NotFound` | 404 | Review id unknown |
| `Review.Unauthorized` | 403 | Not the review's author (and not an admin) |
| `Notification.NotFound` | 404 | Notification id unknown |
| `Notification.Unauthorized` | 403 | Notification belongs to another user |
| `Profile.NotFound` | 404 | User not found |
| `Profile.UpdateFailed` | 500 | Identity failed to save the profile (a server-side failure) |
| `Specialty.NotFound` | 404 | Specialty unknown. Public reads and doctor registration also treat an inactive specialty as unknown; the admin update and delete routes don't |
| `Specialty.NameAlreadyExists` | 409 | Duplicate specialty name (case-insensitive) |
| `Specialty.HasAssignedDoctors` | 409 | Deleting a specialty that doctors use |
| `Admin.DoctorAlreadyApproved` | 409 | Approving an approved doctor |
| `Admin.DoctorAlreadySuspended` | 409 | Suspending a suspended doctor |
| `RateLimit.Exceeded` | 429 | Too many requests from one client to the rate-limited auth endpoints ([§2.7](#27-rate-limiting)). A `Retry-After` header gives the seconds to wait |

Booking can also fail with a 500 whose `title` is `"An error occurred while booking the appointment."` (an unexpected database failure).

## 4. Endpoint index

Auth column: **—** means anonymous; **Any** means any logged-in user.

| # | Method | Path | Auth | Success |
|---|---|---|---|---|
| | **Auth** | | | |
| 1 | POST | `/api/auth/register/patient` | — | 200 `RegisterResponse` |
| 2 | POST | `/api/auth/register/doctor` | — | 200 `RegisterResponse` |
| 3 | POST | `/api/auth/login` | — | 200 `LoginResponse` |
| 4 | POST | `/api/auth/refresh-token` | — | 200 `LoginResponse` |
| 5 | POST | `/api/auth/logout` | — | 204 |
| 6 | POST | `/api/auth/confirm-email` | — | 204 |
| 7 | POST | `/api/auth/forgot-password` | — | 204 |
| 8 | POST | `/api/auth/reset-password` | — | 204 |
| | **Doctors (public)** | | | |
| 9 | GET | `/api/doctors` | — | 200 `DoctorSummaryResponse[]` |
| 10 | GET | `/api/doctors/{id}` | — | 200 `DoctorResponse` |
| 11 | GET | `/api/doctors/{id}/slots?date=` | — | 200 `SlotResponse[]` |
| 12 | GET | `/api/doctors/{id}/reviews` | — | 200 `ReviewResponse[]` |
| | **Doctor (self)** | | | |
| 13 | PUT | `/api/doctors/profile` | Doctor | 204 |
| 14 | GET | `/api/doctors/schedule` | Doctor | 200 `ScheduleResponse[]` |
| 15 | POST | `/api/doctors/schedule` | Doctor | 201 `ScheduleResponse` |
| 16 | PUT | `/api/doctors/schedule/{id}` | Doctor | 200 `ScheduleResponse` |
| 17 | DELETE | `/api/doctors/schedule/{id}` | Doctor | 204 |
| | **Appointments** | | | |
| 18 | POST | `/api/appointments` | Patient | 201 `AppointmentResponse` |
| 19 | GET | `/api/appointments/my?status=&pageNumber=&pageSize=` | Patient | 200 `AppointmentResponse[]` |
| 20 | GET | `/api/appointments/doctor/my` | Doctor | 200 `AppointmentResponse[]` |
| 21 | GET | `/api/appointments/{id}` | Any (owner or admin) | 200 `AppointmentResponse` |
| 22 | POST | `/api/appointments/{id}/cancel` | Patient, Doctor, Admin | 204 |
| 23 | POST | `/api/appointments/{id}/complete` | Doctor | 204 |
| 24 | PUT | `/api/appointments/{id}/notes` | Doctor | 204 |
| 24b | POST | `/api/appointments/{id}/no-show` | Doctor (owner), Admin | 204 |
| | **Reviews** | | | |
| 25 | POST | `/api/reviews` | Patient | 201 `ReviewResponse` |
| 26 | GET | `/api/reviews/doctor/{doctorId}` | — | 200 `ReviewResponse[]` |
| 27 | DELETE | `/api/reviews/{id}` | Any (author or admin) | 204 |
| | **Patient / profile** | | | |
| 28 | GET | `/api/patients/profile` | Patient | 200 `PatientProfileResponse` |
| 29 | PATCH | `/api/patients/profile` | Patient | 204 |
| 30 | GET | `/api/profile/me` | Any | 200 `UserProfileResponse` |
| 31 | PATCH | `/api/profile/me` | Any | 204 |
| | **Notifications** | | | |
| 32 | GET | `/api/notifications/my?unreadOnly=&pageNumber=&pageSize=` | Any | 200 `NotificationResponse[]` + `X-Total-Count` |
| 33 | POST | `/api/notifications/{id}/read` | Any | 204 |
| 34 | POST | `/api/notifications/read-all` | Any | 204 |
| | **Specialties** | | | |
| 35 | GET | `/api/Specialty?includeInactive=` | — (Admin for `includeInactive=true`) | 200 `SpecialtyResponse[]` |
| 36 | GET | `/api/Specialty/{id}` | — | 200 `SpecialtyResponse` |
| 37 | POST / PUT / DELETE | `/api/Specialty[/{id}]` | Admin | *Deprecated: use #40–#43* |
| | **Admin** | | | |
| 38 | GET | `/api/admin/doctors/pending?pageNumber=&pageSize=` | Admin | 200 `DoctorResponse[]` + `X-Total-Count` |
| 39 | POST | `/api/admin/doctors/{id}/approve` | Admin | 204 |
| 39b | POST | `/api/admin/doctors/{id}/suspend` | Admin | 204 |
| 40 | GET | `/api/admin/specialties` | Admin | 200 `SpecialtyResponse[]` (includes inactive) |
| 41 | POST | `/api/admin/specialties` | Admin | 201 `SpecialtyResponse` |
| 42 | PUT | `/api/admin/specialties/{id}` | Admin | 200 `SpecialtyResponse` |
| 43 | DELETE | `/api/admin/specialties/{id}` | Admin | 204 |
| 44 | GET | `/api/admin/stats/overview` | Admin | 200 `OverviewStatsResponse` |
| 45 | GET | `/api/admin/stats/appointments` | Admin | 200 `AppointmentStatsResponse` |

`/api/Specialty` is case-insensitive (`/api/specialty` also works).

## 5. Endpoints

Each entry lists the request, the success response, and the errors that endpoint can return (codes from [§3.3](#33-error-code-catalog)). Every authenticated endpoint can also return 401 (shape E) and, for a wrong role, 403 (shape E); these are not repeated below. Endpoints marked anonymous are the only ones that work without a token.

### 5.1 Auth

**1. `POST /api/auth/register/patient`** (anonymous)
- **Body:** `RegisterPatientRequest`. Rules:

  | Field | Rule |
  |---|---|
  | `firstName`, `lastName` | Required, ≤ 50 chars |
  | `email` | Required, valid email |
  | `password` | Required, ≥ 8 chars, with upper case, lower case, a digit and a special character |
  | `phoneNumber` | Required, E.164 with 7–15 digits (`^\+?[1-9]\d{6,14}$`). Every phone field in the API uses this rule |
  | `gender` | Always send it. It is not validated: if omitted, the server stores `Male` |
  | `dateOfBirth` | Optional; the user must be at least 18 |
  | `emergencyContactPhone` | Optional, E.164 |
- **200:** `RegisterResponse`. A confirmation email is sent; the link opens the frontend route `/#/confirm-email` ([§8](#8-frontend-routes-the-backend-depends-on)). If the email fails to send, registration still succeeds.
- **Errors:**
  - 400 validation (B)
  - 400 (C) `Auth.RegistrationFailed` (Identity rejected the user)
  - 409 `Auth.EmailAlreadyExists`
  - 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting))
  - 500 `Auth.RegistrationFailed` (unexpected server failure)

**2. `POST /api/auth/register/doctor`** (anonymous)
- **Body:** `RegisterDoctorRequest`. Same rules for name, email, password, phone and date of birth, plus:

  | Field | Rule |
  |---|---|
  | `specialtyId` | Required, > 0, an existing active specialty (else 404) |
  | `yearsOfExperience` | 0–70 |
  | `consultationFee` | > 0, ≤ 10 000 (same as a profile update) |
  | `bio` | Optional, ≤ 500 |
  | `clinicAddress` | Optional, ≤ 300 |
  | `clinicCity` | Optional, ≤ 100 |
- **200:** `RegisterResponse`. The doctor must confirm their email; an admin must approve them before they appear in search.
- **Errors:**
  - 400 (B)
  - 404 `Specialty.NotFound` (unknown or inactive `specialtyId`)
  - 409 `Auth.EmailAlreadyExists`
  - 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting))
  - 500 `Auth.RegistrationFailed` (unexpected server failure)

**3. `POST /api/auth/login`** (anonymous)
- **Body:** `{ "email": string, "password": string }`. `email` is required and must be a valid email; `password` is required (its strength isn't checked here).
- **200:** `LoginResponse`
- **Errors:**
  - 400 (B) for an empty or malformed field
  - 401 `Auth.InvalidCredentials`, `Auth.AccountLockedOut`, `Auth.AccountNotActive`, `Auth.EmailNotConfirmed`
  - 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting))

**4. `POST /api/auth/refresh-token`** (anonymous)
- **Body:** `{ "refreshToken": string }`
- **200:** `LoginResponse` with a **new** access and refresh token. Follow the rules in [§2.3](#23-refresh-rules-frontend-must-follow-these).
- **Errors:** 401 `Auth.InvalidRefreshToken`

**5. `POST /api/auth/logout`** (anonymous; an access token is optional)
- **Body:** `{ "refreshToken": string }`
- **204**
- **Errors:** 401 `Auth.InvalidRefreshToken` (the token is unknown, or an access token was sent and the refresh token isn't that user's)

**6. `POST /api/auth/confirm-email`** (anonymous)
- **Body:** `{ "userId": "<guid>", "token": string }`, both taken from the link query string as they are ([§8](#8-frontend-routes-the-backend-depends-on)).
- **204**
- **Errors:**
  - 404 `Auth.UserNotFound`
  - 400 (C) `Auth.InvalidEmailConfirmationToken`
  - 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting))

**7. `POST /api/auth/forgot-password`** (anonymous)
- **Body:** `{ "email": string }`
- **204** whether or not the email exists, so callers can't discover registered emails. The email links to `/#/reset-password`.
- **Errors:** 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting)); otherwise none for a well-formed body. If the email fails to send, the failure is logged and the response is still 204, so a mail outage doesn't reveal which emails are registered.

**8. `POST /api/auth/reset-password`** (anonymous)
- **Body:** `{ "userId": "<guid>", "token": string, "newPassword": string }`. `userId` and `token` are required; `newPassword` follows the registration password rules (≥ 8 chars, upper case, lower case, a digit and a special character).
- **204.** All of the user's sessions are revoked, so they must log in again.
- **Errors:**
  - 400 (B) for a missing field or a weak password
  - 404 `Auth.UserNotFound` (also for a suspended account)
  - 400 (C) `Auth.InvalidPasswordResetToken`
  - 429 `RateLimit.Exceeded` ([§2.7](#27-rate-limiting))

### 5.2 Doctors (public)

**9. `GET /api/doctors`**

| Query | Type | Default | Rule |
|---|---|---|---|
| `name` | string | `""` | Substring of the full name, case-insensitive |
| `specialtyId` | int | — | > 0 |
| `city` | string | — | Substring, ≤ 100 chars |
| `sortBy` | `DoctorSortBy` | `Rating` | `Rating` (desc), `Fee` (asc), `Experience` (desc) |
| `pageNumber` | int | 1 | ≥ 1 |
| `pageSize` | int | 10 | 1–50 |

- **200:** `DoctorSummaryResponse[]`. Only approved and active doctors. No total count is returned; there may be another page if the array is full.
- **Errors:** 400 (B)

**10. `GET /api/doctors/{id}`**
- **200:** `DoctorResponse`
- **Errors:** 404 `Doctor.NotFound`
- Unapproved and suspended doctors return 404.

**11. `GET /api/doctors/{id}/slots?date=yyyy-MM-dd`**
- `date` is **required**.
- **200:** `SlotResponse[]` with only `Available` slots, ordered by start time.
- An unknown, unapproved or suspended doctor returns `[]`.
- **Errors:** 400 (D, key `date`) when `date` is missing or not a date
- Slots exist for the next 14 days (counting today in the clinic's time zone). A daily job extends the window, so it never runs out.

**12. `GET /api/doctors/{id}/reviews?pageNumber=&pageSize=`**
- **Paging:** default 1/10; `pageSize` 1–20.
- **200:** `ReviewResponse[]`, newest first. An unknown doctor returns `[]`.
- **Errors:** 400 (B)
- Equivalent to #26, which returns 404 for an unknown doctor. **Prefer #26.**

### 5.3 Doctor (self-service)

**13. `PUT /api/doctors/profile`** (Doctor)
- **Body:** `UpdateDoctorProfileRequest`. `consultationFee` is optional (when sent: > 0, ≤ 10 000); omitting it keeps the current fee. `bio` ≤ 500, `clinicAddress` ≤ 300, `clinicCity` ≤ 100. A `null` text field keeps its current value.
- **204**
- **Errors:**
  - 400 (B)
  - 404 `Doctor.ProfileNotFound`

**14. `GET /api/doctors/schedule`** (Doctor)
- **200:** `ScheduleResponse[]`, ordered by weekday.
- **Errors:** 404 `Doctor.ProfileNotFound`

**15. `POST /api/doctors/schedule`** (Doctor)
- **Body:** `CreateScheduleRequest`. Rules:
  - `dayOfWeek` is required.
  - `startTime` and `endTime` are required, with `endTime` after `startTime`.
  - `slotDurationMinutes` is 10–120 (default 30).
  - The window must fit at least one slot.
- **201:** `ScheduleResponse`. Slots for the next 14 days are generated, skipping times that overlap a slot the doctor already has. A daily job then keeps every active schedule's slots filled 14 days ahead. If that weekday has an *inactive* schedule (for example one kept for its history when deleted), it is reused: the response has its id and `isActive: true`.
- **Errors:**
  - 400 (B)
  - 404 `Doctor.ProfileNotFound`
  - 409 `Doctor.ScheduleDayAlreadyExists` (an active schedule for that weekday exists)

**16. `PUT /api/doctors/schedule/{id}`** (Doctor, own schedule)
- **Body:** `UpdateScheduleRequest`. All fields are optional; omitted fields keep their value. `slotDurationMinutes` is 10–120. If both times are sent, `endTime` must be after `startTime`.
- **200:** `ScheduleResponse`. Future free slots are regenerated in one transaction. Booked slots, and slots a cancelled appointment points to, are kept; new slots skip times that overlap them. Setting `isActive: false` removes the free slots and generates none.
- **Errors:**
  - 400 (B), or (C) `Doctor.InvalidScheduleTime`
  - 404 `Doctor.ScheduleNotFound` (also when the schedule is another doctor's)

**17. `DELETE /api/doctors/schedule/{id}`** (Doctor, own schedule)
- **204.** The schedule and its free slots are removed. If past or cancelled appointments still point to its slots, the schedule is deactivated (`isActive: false`) instead, to keep that history; creating a schedule for that weekday later reuses it.
- **Errors:**
  - 404 `Doctor.ScheduleNotFound` (also when the schedule is another doctor's)
  - 409 `Doctor.CannotDeleteScheduleWithBookedSlots` (upcoming appointments are booked on it; cancel them first)

### 5.4 Appointments

**18. `POST /api/appointments`** (Patient)
- **Body:** `{ "slotId": int (> 0), "reasonForVisit": string | null (≤ 500) }`
- **201:** `AppointmentResponse`, with status `Confirmed`. Notifications and a confirmation email are sent.
- **Errors:**
  - 400 (B)
  - 400 (C) `Appointment.SlotInPast` (the slot has started on the clinic's clock)
  - 404 `Patient.ProfileNotFound`, `Appointment.SlotNotFound` (also for a slot of an unapproved or suspended doctor)
  - 409 `Appointment.SlotNotAvailable`, `Appointment.SlotAlreadyBooked`
  - 500
- **Frontend:** on 409, refresh the slot list.

**19. `GET /api/appointments/my`** (Patient)

| Query | Type | Default | Rule |
|---|---|---|---|
| `status` | `AppointmentStatus` | — | Optional filter |
| `pageNumber` | int | 1 | ≥ 1 |
| `pageSize` | int | 10 | 1–50 |

- **200:** `AppointmentResponse[]`, newest first.
- **Errors:**
  - 400 (B)
  - 404 `Patient.ProfileNotFound`

**20. `GET /api/appointments/doctor/my`** (Doctor)
- Same query parameters and defaults as #19.
- **200:** `AppointmentResponse[]`, newest first.
- **Errors:**
  - 400 (B)
  - 404 `Doctor.ProfileNotFound`

**21. `GET /api/appointments/{id}`** (owning patient, owning doctor, or admin)
- **200:** `AppointmentResponse`
- **Errors:**
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`

**22. `POST /api/appointments/{id}/cancel`** (Patient, Doctor or Admin; the behaviour depends on the caller's role)
- **Body:** `{ "cancellationReason": string | null (≤ 500) }`. **A body is required**; send `{}` if there is no reason.
- **204.** The slot becomes available again, and both parties are notified. The notifications follow a few seconds later (§1).
- **Patient rules:**
  - own appointment only
  - not already cancelled, completed, marked as a no-show, in progress or rescheduled
  - at least 2 hours before the start (on the clinic's clock)
- **Doctor rules:**
  - own appointment only
  - not cancelled, completed, a no-show, in progress or rescheduled
  - any time
- **Admin rules:**
  - any appointment that is not cancelled, completed, a no-show, in progress or rescheduled
- **Errors:**
  - 400 (B)
  - 400 (C) `Appointment.CancellationWindowExpired` (patient)
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Patient.ProfileNotFound`, `Doctor.ProfileNotFound`
  - 409 `Appointment.AlreadyCancelled`, `Appointment.AlreadyCompleted`, `Appointment.AlreadyNoShow`, `Appointment.InvalidStatusTransition` (in progress or rescheduled)

**23. `POST /api/appointments/{id}/complete`** (Doctor, own appointment)
- **204.** The patient can now review the appointment.
- **Errors:**
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Doctor.ProfileNotFound`
  - 409 `Appointment.AlreadyCompleted`, `Appointment.AlreadyNoShow`
  - 400 (C) `Appointment.CannotCompleteBeforeStart` (the appointment hasn't started yet)
  - 409 `Appointment.CannotCompleteCancelled`
  - 409 `Appointment.InvalidStatusTransition` (checked in but not started, rescheduled)

**24. `PUT /api/appointments/{id}/notes`** (Doctor, own appointment)
- **Body:** `{ "notes": string | null (≤ 4000) }`. This replaces the notes; `null` clears them.
- **204**
- **Errors:**
  - 400 (B)
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Doctor.ProfileNotFound`
  - 409 `Appointment.DoctorNotesNotAllowed` (cancelled or rescheduled)

**24b. `POST /api/appointments/{id}/no-show`** (the owning doctor, or an admin)
- No body.
- **204.** A `Confirmed` appointment becomes `NoShow`, and the patient gets a `SystemAlert` notification ("Missed Appointment"). The slot stays booked. Nothing is marked automatically.
- **Rules:** at least **15 minutes** after the start time, on the clinic's clock.
- **Errors:**
  - 400 (C) `Appointment.NoShowTooEarly`
  - 403 `Appointment.Unauthorized` (another doctor's appointment)
  - 404 `Appointment.NotFound`
  - 409 `Appointment.AlreadyCancelled`, `Appointment.AlreadyCompleted`, `Appointment.AlreadyNoShow`, `Appointment.InvalidStatusTransition` (any other status but `Confirmed`)
- A no-show appointment can't be cancelled, completed or reviewed; doctor notes can still be added.

### 5.5 Reviews

**25. `POST /api/reviews`** (Patient)
- **Body:** `{ "appointmentId": int (>0), "rating": int (1–5), "comment": string | null (≤ 300) }`
- **201:** `ReviewResponse`. The doctor's average rating is updated.
- **Errors:**
  - 400 (B), or (C) `Appointment.ReviewNotAllowed`
  - 403 `Appointment.Unauthorized` (another patient's appointment, whether or not it has been reviewed)
  - 404 `Patient.ProfileNotFound`, `Appointment.NotFound`, `Doctor.NotFound`
  - 409 `Appointment.ReviewAlreadyExists` (the caller's own appointment only)
- **Check order:** appointment exists → belongs to the caller → completed → not yet reviewed.
- **Frontend:** show the review button only when `AppointmentResponse.status == "Completed"` and `hasReview == false`.

**26. `GET /api/reviews/doctor/{doctorId}?pageNumber=&pageSize=`** (anonymous)
- **Paging:** default 1/10; `pageSize` 1–20.
- **200:** `ReviewResponse[]`, newest first. Only visible reviews.
- **Errors:**
  - 400 (B)
  - 404 `Doctor.NotFound`

**27. `DELETE /api/reviews/{id}`** (the review's author, or an admin)
- **204**
- **Errors:**
  - 403 `Review.Unauthorized`
  - 404 `Review.NotFound`, `Doctor.NotFound`

### 5.6 Patient profile and user profile

**28. `GET /api/patients/profile`** (Patient)
- **200:** `PatientProfileResponse`
- **Errors:** 404 `Patient.ProfileNotFound`

**29. `PATCH /api/patients/profile`** (Patient)
- **Body:** `UpdatePatientProfileRequest`. All fields are optional:
  - `bloodType` must be a valid enum value
  - `allergies` and `chronicConditions` ≤ 500
  - `emergencyContactName` ≤ 100
  - `emergencyContactPhone` is E.164 with 7–15 digits, as at registration
- **204**
- **Errors:**
  - 400 (B)
  - 404 `Patient.ProfileNotFound`

**30. `GET /api/profile/me`** (any logged-in user)
- **200:** `UserProfileResponse`
- **Errors:** 404 `Profile.NotFound`

**31. `PATCH /api/profile/me`** (any logged-in user)
- **Body:** `UpdateUserProfileRequest`. All fields are optional:
  - `firstName` and `lastName` ≤ 50
  - `phoneNumber` is E.164 with 7–15 digits, as at registration
  - `profilePictureUrl` is an absolute `http` or `https` URL
  - `dateOfBirth` is in the past
- **204**
- **Errors:**
  - 400 (B)
  - 404 `Profile.NotFound`
  - 500 `Profile.UpdateFailed`

### 5.7 Notifications

**32. `GET /api/notifications/my`**

| Query | Type | Default | Rule |
|---|---|---|---|
| `unreadOnly` | bool | `false` | Only unread notifications |
| `pageNumber` | int | 1 | ≥ 1 |
| `pageSize` | int | 50 | 1–100 |

- **200:** `NotificationResponse[]`, newest first: one page. The `X-Total-Count` header holds the number of matching notifications (all pages), e.g. for an unread badge with `unreadOnly=true&pageSize=1`.
- **Errors:** 400 (B)

**33. `POST /api/notifications/{id}/read`**
- **204**
- **Errors:**
  - 403 `Notification.Unauthorized`
  - 404 `Notification.NotFound`

**34. `POST /api/notifications/read-all`**
- **204**

`relatedEntityId` holds the appointment id for every appointment-related notification. That includes the doctor's "New Appointment Booked", whose type is `SystemAlert`. It is `null` for account notifications (doctor approved or suspended).

### 5.8 Specialties

**35. `GET /api/Specialty?includeInactive=false`**
- **200:** `SpecialtyResponse[]`, active specialties only.
- `includeInactive=true` requires an admin token; anyone else gets 403 (shape E).

**36. `GET /api/Specialty/{id}`**
- **200:** `SpecialtyResponse` (active only)
- **Errors:** 404 `Specialty.NotFound`

**37. `POST /api/Specialty`, `PUT /api/Specialty/{id}`, `DELETE /api/Specialty/{id}`** (Admin)
- **Deprecated.** These are identical to #41–#43. Use the `/api/admin/specialties` routes; these will be removed.

### 5.9 Admin (all endpoints require the Admin role)

**38. `GET /api/admin/doctors/pending`**

| Query | Type | Default | Rule |
|---|---|---|---|
| `pageNumber` | int | 1 | ≥ 1 |
| `pageSize` | int | — (all) | 1–100. Omit it to get every pending doctor |

- **200:** `DoctorResponse[]`, doctors awaiting approval (suspended applicants are left out), oldest application first. The `X-Total-Count` header holds the total.
- **Errors:** 400 (B)

**39. `POST /api/admin/doctors/{id}/approve`**
- **204.** The doctor is notified.
- **Errors:**
  - 404 `Doctor.NotFound`
  - 409 `Admin.DoctorAlreadyApproved`

**39b. `POST /api/admin/doctors/{id}/suspend`**
- **204.** The doctor's refresh tokens are revoked; their current access token stays valid for up to 15 min.
- **Errors:**
  - 404 `Doctor.NotFound`
  - 409 `Admin.DoctorAlreadySuspended`

**40. `GET /api/admin/specialties`**
- **200:** `SpecialtyResponse[]`, including inactive specialties.

**41. `POST /api/admin/specialties`**
- **Body:** `SpecialtyRequest`. Rules:
  - `name` is required, ≤ 100, and unique.
  - `description` ≤ 500.
  - `iconUrl` must be an absolute `http` or `https` URL.
  - `isActive` is optional; a new specialty is active unless `false` is sent.
- **201:** `SpecialtyResponse`
- **Errors:**
  - 400 (B)
  - 409 `Specialty.NameAlreadyExists`

**42. `PUT /api/admin/specialties/{id}`**
- **Body:** `SpecialtyRequest`. Send `isActive: false` to deactivate and `isActive: true` to reactivate; omitting it (or `null`) keeps the current state. Works on inactive specialties.
- **200:** `SpecialtyResponse`
- **Errors:**
  - 400 (B)
  - 404 `Specialty.NotFound`
  - 409 `Specialty.NameAlreadyExists`

**43. `DELETE /api/admin/specialties/{id}`**
- **204.** Works on inactive specialties too.
- **Errors:**
  - 404 `Specialty.NotFound`
  - 409 `Specialty.HasAssignedDoctors`

**44. `GET /api/admin/stats/overview`**
- **200:** `OverviewStatsResponse`. `pendingApprovals` counts the same doctors as #38 (suspended applicants are left out).

**45. `GET /api/admin/stats/appointments`**
- **200:** `AppointmentStatsResponse`

## 6. Data types

Types use TypeScript-style notation. `?` means the field may be omitted in a request; `| null` means the value may be null.

### Requests

| Type | Fields |
|---|---|
| `RegisterPatientRequest` | `firstName: string`, `lastName: string`, `email: string`, `password: string`, `phoneNumber: string`, `gender: Gender`, `dateOfBirth?: DateOnly \| null`, `bloodType?: BloodType \| null`, `allergies?: string \| null`, `chronicConditions?: string \| null`, `emergencyContactName?: string \| null`, `emergencyContactPhone?: string \| null` |
| `RegisterDoctorRequest` | `firstName`, `lastName`, `email`, `password`, `phoneNumber: string`, `gender: Gender`, `dateOfBirth?: DateOnly \| null`, `specialtyId: number`, `bio?: string \| null`, `yearsOfExperience: number`, `consultationFee: number`, `clinicAddress?: string \| null`, `clinicCity?: string \| null` |
| `LoginRequest` | `email: string`, `password: string` |
| `RefreshTokenRequest` | `refreshToken: string` |
| `ConfirmEmailRequest` | `userId: string`, `token: string` |
| `ForgotPasswordRequest` | `email: string` |
| `ResetPasswordRequest` | `userId: string`, `token: string`, `newPassword: string` |
| `UpdateDoctorProfileRequest` | `bio?: string \| null`, `consultationFee?: number \| null`, `clinicAddress?: string \| null`, `clinicCity?: string \| null` |
| `CreateScheduleRequest` | `dayOfWeek: DayOfWeek`, `startTime: TimeOnly`, `endTime: TimeOnly`, `slotDurationMinutes?: number` (default 30) |
| `UpdateScheduleRequest` | `startTime?: TimeOnly`, `endTime?: TimeOnly`, `slotDurationMinutes?: number`, `isActive?: boolean` |
| `BookAppointmentRequest` | `slotId: number`, `reasonForVisit?: string \| null` |
| `CancelAppointmentRequest` | `cancellationReason?: string \| null` |
| `DoctorNotesRequest` | `notes?: string \| null` |
| `SubmitReviewRequest` | `appointmentId: number`, `rating: number`, `comment?: string \| null` |
| `UpdatePatientProfileRequest` | `bloodType?`, `allergies?`, `chronicConditions?`, `emergencyContactName?`, `emergencyContactPhone?` (all nullable) |
| `UpdateUserProfileRequest` | `firstName?`, `lastName?`, `phoneNumber?`, `profilePictureUrl?`, `gender?: Gender`, `dateOfBirth?: DateOnly` (all nullable) |
| `SpecialtyRequest` | `name: string`, `description?: string \| null`, `iconUrl?: string \| null`, `isActive?: boolean \| null` |

### Responses

| Type | Fields |
|---|---|
| `RegisterResponse` | `message: string`, `email: string` |
| `LoginResponse` | `accessToken: string`, `refreshToken: string`, `expiresAt: string` (UTC, with `Z`), `email: string`, `fullName: string`, `role: Role` |
| `DoctorSummaryResponse` | `id`, `fullName`, `specialtyName`, `yearsOfExperience`, `consultationFee`, `averageRating`, `clinicCity: string \| null` |
| `DoctorResponse` | `id`, `fullName`, `specialtyName`, `bio: string \| null`, `yearsOfExperience`, `consultationFee`, `averageRating`, `totalReviews`, `clinicAddress: string \| null`, `clinicCity: string \| null` |
| `SlotResponse` | `id`, `date: DateOnly`, `startTime: TimeOnly`, `endTime: TimeOnly`, `status: SlotStatus` |
| `ScheduleResponse` | `id`, `dayOfWeek: DayOfWeek`, `startTime`, `endTime`, `slotDurationMinutes`, `isActive: boolean` |
| `AppointmentResponse` | `id`, `slotId`, `doctorProfileId`, `patientProfileId`, `doctorName`, `patientName`, `appointmentDate: DateOnly`, `startTime`, `endTime`, `status: AppointmentStatus`, `reasonForVisit`, `doctorNotes`, `bookedAt: Timestamp`, `cancelledAt: Timestamp \| null`, `cancellationReason`, `cancelledBy: CancelledBy \| null`, `hasReview: boolean` |
| `ReviewResponse` | `id`, `rating`, `comment: string \| null`, `createdAt: Timestamp`, `isVisible: boolean`, `patientName` |
| `PatientProfileResponse` | `id`, `fullName`, `email`, `phoneNumber`, `bloodType: BloodType \| null`, `allergies`, `chronicConditions`, `emergencyContactName`, `emergencyContactPhone` (strings may be null) |
| `UserProfileResponse` | `id: string (GUID)`, `firstName`, `lastName`, `email`, `phoneNumber: string \| null`, `profilePictureUrl: string \| null`, `gender: Gender`, `dateOfBirth: DateOnly \| null`, `role: Role`, `createdAt: Timestamp` |
| `NotificationResponse` | `id`, `title`, `message`, `type: NotificationType`, `isRead: boolean`, `createdAt: Timestamp`, `relatedEntityId: number \| null` |
| `SpecialtyResponse` | `id`, `name`, `description: string \| null`, `iconUrl: string \| null`, `isActive: boolean` |
| `OverviewStatsResponse` | `totalDoctors`, `totalPatients`, `totalAppointments`, `totalSpecialties`, `pendingApprovals`, `totalReviews` (numbers) |
| `AppointmentStatsResponse` | `pending`, `confirmed`, `completed`, `cancelled`, `noShow` (numbers) |

`Timestamp` is a UTC date-time string ending in `Z` ([§1](#1-general-conventions)).

## 7. Enums

| Enum | Values |
|---|---|
| `Role` | `Admin`, `Doctor`, `Patient` |
| `AppointmentStatus` | `Pending`, `Confirmed`, `Cancelled`, `Completed`, `NoShow`, `CheckedIn`, `InProgress`, `Rescheduled`. `Pending` never occurs; `NoShow` is set by #24b. `CheckedIn`, `InProgress` and `Rescheduled` (since 4.0.0-p0, [docs/v2/adr/0004](v2/adr/0004-appointment-state-machine.md)) are not produced by any route yet; they arrive with V2's check-in, queue and reschedule routes. Display unknown values rather than failing |
| `SlotStatus` | `Available`, `Booked`, `Blocked` |
| `CancelledBy` | `Patient`, `Doctor`, `Admin` |
| `NotificationType` | `AppointmentReminder`, `AppointmentConfirmed`, `AppointmentCancelled`, `AppointmentCompleted`, `SystemAlert` |
| `Gender` | `Male`, `Female` |
| `BloodType` | `APositive`, `ANegative`, `BPositive`, `BNegative`, `OPositive`, `ONegative`, `ABPositive`, `ABNegative` |
| `DoctorSortBy` | `Rating`, `Fee`, `Experience` |
| `DayOfWeek` | `Sunday`, `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday` |

## 8. Frontend routes the backend depends on

The backend builds links in emails from `Frontend:BaseUrl` (default `http://localhost:3000`). The frontend **must** provide these hash routes:

| Route | Sent in | Query parameters | Frontend action |
|---|---|---|---|
| `/#/confirm-email` | Registration email | `userId`, `token` | Read both parameters with `URLSearchParams` (this decodes them once) and `POST /api/auth/confirm-email` |
| `/#/reset-password` | Forgot-password email | `userId`, `token` | Ask for the new password and `POST /api/auth/reset-password` with both parameters |

If the frontend changes its routing (e.g. stops using hash routing), this table and `EmailService` must change together.

## 9. Pending contract changes

These are known issues ([REVIEW.md](REVIEW.md)) whose fix **will change the contract**. None of them is active yet. Each one moves into the changelog when it ships, and the frontend should be ready for it:

| Review | Change | Frontend impact |
|---|---|---|
| — | Removal of the deprecated `/api/Specialty` write routes (#37) | Use `/api/admin/specialties` |
| — | Removal of `GET /api/doctors/{id}/reviews` (#12), a duplicate of #26 kept for compatibility. #26 differs only by returning 404 for an unknown doctor | Use #26 |

## 10. Changelog

| Version | Date | Changes |
|---|---|---|
| 1.0.0 | 2026-10-09 | First version of the contract. Behaviour changes that shipped the same day and that the frontend may notice: specialty write routes require Admin (C1); `GET /api/appointments/{id}` returns 403 for non-owners (C4); doctors can no longer delete reviews (H2); refresh fails for suspended users (H3); reuse of a rotated refresh token can revoke all sessions (H7); login lockout after 5 failures with the new code `Auth.AccountLockedOut` (M6); logout only accepts the caller's own refresh token (L7); new code `Appointment.SlotNotFound` replaces `Appointment.NotFound` for an unknown slot (L4); `PUT /api/doctors/schedule/{id}` no longer resets omitted fields (H5); appointment lists now fill `doctorName`/`patientName` (NM4); email links use `Frontend:BaseUrl` |
| 1.0.1 | 2026-10-17 | Bug fix (NL22): `POST /api/reviews` for another patient's appointment now always returns 403 `Appointment.Unauthorized`. Before, it returned 409 `Appointment.ReviewAlreadyExists` when that appointment already had a review, which revealed whether it had been reviewed. 409 now applies only to the caller's own appointment. No change for a frontend that only offers reviews on the patient's own completed appointments |
| 1.0.2 | 2026-10-17 | Bug fix (NL24): `POST /api/auth/forgot-password` returns 204 even when the reset email fails to send. Before, it returned 500 in that case, which only happened for registered, active emails and so revealed them. No frontend change is needed if it already shows the same message for every 204 |
| 2.0.0 | 2026-10-17 | **Breaking: status codes.** Expected business errors no longer return 500 (M3): duplicates and state conflicts are 409 (`Auth.EmailAlreadyExists`, `Doctor.ScheduleDayAlreadyExists`, `Admin.DoctorAlreadyApproved`/`Suspended`, `Specialty.NameAlreadyExists`/`HasAssignedDoctors`, `Appointment.CannotCompleteCancelled`, `Appointment.DoctorNotesNotAllowed`); bad confirmation and reset tokens and a registration Identity rejects are 400 (shape C); doctor registration with an unknown specialty is 404 `Specialty.NotFound`. `Auth.RegistrationFailed` is 500 only for real server failures. Error codes are unchanged. **Behaviour:** unapproved and suspended doctors are hidden from patients (profile 404, empty slots, booking 404 `Appointment.SlotNotFound`) (M1); completing an appointment before it starts is 400 `Appointment.CannotCompleteBeforeStart`, and an admin cancelling a completed appointment gets `Appointment.AlreadyCompleted` (M14); deleting a schedule with upcoming bookings is 409 `Doctor.CannotDeleteScheduleWithBookedSlots`, one with only past history is deactivated, and schedule updates no longer fail with 500 (H4). **Request:** `UpdateDoctorProfileRequest.consultationFee` is optional (M15). **Frontend:** errors were already mapped by code, so messages keep working; a check on `status === 500` for these codes would need changing |
| 3.0.0 | 2026-10-17 | **Breaking: error bodies (M5, NL9).** Every error is now `application/problem+json`. 401 from the auth middleware, 403 for a wrong role, 404 for an unknown route and unhandled 500s used to have an empty body and now carry ProblemDetails without an error code (shape E); every 400 (validation, business rule or binding) has `type`, `status` and `traceId`. **Breaking: authentication by default (NM5).** Endpoints without an explicit rule now require a logged-in user; every endpoint §4 lists as anonymous still is, and an anonymous request to an unknown route gets 401 instead of 404. **Breaking: new 400s (H6, M13, NL11).** Appointment lists (#19, #20) default to page 1 of 10 (before: `[]` for patients and one item for doctors) and reject `pageNumber` < 1 or `pageSize` outside 1–50 (before: 500); new validation on booking (`slotId` > 0, `reasonForVisit` ≤ 500), cancel (`cancellationReason` ≤ 500), notes (≤ 4000), login (valid email, non-empty password), reset password (registration password rules) and doctor registration (`specialtyId`, `yearsOfExperience` 0–70, `consultationFee` > 0 and ≤ 10 000, text lengths); profile phone fields use the registration E.164 rule, and every phone field now needs 7–15 digits; `GET /api/doctors/{id}/slots` without `date` is 400 (before: `[]`). **Paging (NL2):** notifications (#32) default to the newest 50 (`pageSize` ≤ 100) and pending doctors (#38) accept optional paging; both send the total in an `X-Total-Count` header, which CORS exposes. **New:** `POST /api/appointments/{id}/no-show` (#24b) for the owning doctor or an admin, 15 minutes after the start; codes `Appointment.NoShowTooEarly` (400) and `Appointment.AlreadyNoShow` (409, also from cancel and complete) (M14). `SpecialtyRequest.isActive` deactivates or reactivates a specialty, and the admin update and delete routes reach inactive specialties (L6). **Behaviour:** time rules use the clinic's time zone instead of UTC, so past slots are no longer bookable for 2–3 hours and the 2-hour cancellation window is exact (M2); slots are rolled forward daily and no longer run out after 14 days (H8); `pendingApprovals` leaves out suspended applicants, like the pending list (NL23). **Frontend:** handle a JSON body on 401/403/404/500 and branch on `status` when `title` isn't an error code; send valid paging; read `X-Total-Count` if you need totals; add the no-show action |
| 3.1.0 | 2026-10-18 | No breaking changes: every request that worked before still gets the same response. **New: rate limiting (M6).** Register (#1, #2), login (#3), confirm email (#6), forgot password (#7) and reset password (#8) share a per-client-IP limit, 10 requests per 60 s by default ([§2.7](#27-rate-limiting)). Over it: 429 with the new code `RateLimit.Exceeded` and a `Retry-After` header. Refresh and logout are not limited. **Relaxed auth rule: logout (L7).** `POST /api/auth/logout` (#5) no longer requires an access token, so it works after the access token has expired; with an access token, the refresh token must still be the caller's. **Behaviour (H4):** a free slot kept because a cancelled appointment points to it is now `Blocked` while the doctor's schedule no longer covers it (hours moved away, or the schedule deactivated), so it no longer appears in `GET /api/doctors/{id}/slots` and booking it returns 409 `Appointment.SlotNotAvailable`. It is reopened if the hours cover it again. Cancelling an appointment outside the doctor's current hours blocks its slot instead of freeing it. **Behaviour (L8):** a doctor's `averageRating` and `totalReviews` are updated in the same transaction as the review insert or delete, so concurrent reviews can't leave them stale. **Frontend:** show a "try again in N seconds" message for 429 `RateLimit.Exceeded` on the auth forms; logout can be called without refreshing first |
| 3.1.1 | 2026-10-18 | Bug fix: a 400 from `PUT /api/appointments/{id}/notes` (#24) keys the validation error as `Notes`, like every other validation error (§3.1 B: PascalCase property names). It was `notes`. The request body is unchanged (`{ "notes": … }`). Internal cleanup with no other observable change (REVIEW.md group B). §9 now also lists the removal of #12, which duplicates #26 |
| 3.1.2 | 2026-10-18 | Bug fix: CORS now exposes the `Retry-After` header, so a frontend on another origin can read the wait time of a 429 `RateLimit.Exceeded` (§2.7). Before, the browser hid it, and only same-origin deployments (e.g. behind the compose nginx proxy) could read it. No other change |
| 4.0.0-p0 | 2026-10-23 | **Pre-release on the `v2` branch (V2 line)**; the V2 frontend is its only client. Bug fix: `profilePictureUrl` (#31) and `iconUrl` (#41, #42) must be absolute `http` or `https` URLs. Before, the rule accepted any absolute URI, including `javascript:` and `file:`, and on Linux (the Docker image) a path such as `/images/me.png`, which .NET reads as a `file://` URI. Those values now return 400 with a validation error on the field; the message is "... must be an absolute http or https URL." **Breaking (format): every timestamp ends in `Z`** (P0 PR 4, REVIEW.md §4.1). Timestamps read from the database (`bookedAt`, `cancelledAt`, `createdAt`, …) used to be sent without it; only `LoginResponse.expiresAt` had it. The values are unchanged and still UTC. A client that appends a missing `Z` before parsing, like §12's `parseTimestamp`, needs no change; one that parsed the bare value as local time now gets the correct instant. The date-of-birth rules (#1, #2, #31) now compare with the clinic's date (`Clinic:TimeZoneId`) instead of the UTC date, which differs only for a few hours around midnight **Breaking (enum): `AppointmentStatus` gains `CheckedIn`, `InProgress` and `Rescheduled`** (P0 PR 7, [docs/v2/adr/0004](v2/adr/0004-appointment-state-machine.md)). No route produces them yet; the status filter of #19 and #20 accepts them. A client with an exhaustive switch over the statuses needs a default case. New code `Appointment.InvalidStatusTransition` (409) for a status change the rules don't allow when no older code applies; the only appointments the API creates are `Confirmed`, so in P0 it can only come from a `Pending` row (completing or marking one as a no-show used to succeed). Every other route, status code and error code is unchanged. **Bug fix:** `hasReview` is correct in `GET /api/appointments/{id}` (#21) and the doctor's list (#20); both always sent `false` before, because the review wasn't loaded **Behaviour: notifications and emails are asynchronous** (P0 PR 9, [06 §5](v2/06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)). They are written to an outbox with the change and sent by a background job about 2 s later, instead of inside the request; every response, status code and error code is unchanged. A client that reads `/api/notifications/my` right after an action may not see the new notification yet (§1). Bug fix (REVIEW.md NL24): `POST /api/auth/forgot-password` (#7) now takes about as long for a registered email as for an unknown one, because the request no longer waits for SMTP; a failed confirmation or reset email is retried instead of being lost. **New (operations): health probes** (P0 PR 11, §1). `GET /health/live` and `GET /health/ready` join `GET /health`, which is unchanged and now an alias of `/health/live`. They are for orchestrators and load balancers; no frontend route uses them |

## 11. How to change this contract

1. **One PR per contract change, in the backend repo**, that updates the code, this file (§4–§7 and the changelog) and `openapi.json` together. Mirror the same text into the frontend repo's copy in a linked PR.
2. **Additive changes** (a new endpoint, a new optional request field, a new response field, a new error code) are a minor version bump (1.**x**.0). The frontend must ignore unknown response fields.
3. **Breaking changes** (removing or renaming a field or route, changing a type, status code, enum value, auth rule or required field) are a major version bump (**x**.0.0). List them in §9 first and agree on a date with the frontend. Ship backward-compatible where possible: add the new form, migrate the frontend, then remove the old form.
4. **Bug fixes that change observed behaviour** (like those in §9) are listed in the changelog even when the code fix is small.
5. **Regenerating `openapi.json`:** run the API in Development and save `GET /openapi/v1.json` (see [SETUP_AND_DEPLOYMENT.md](SETUP_AND_DEPLOYMENT.md#regenerating-openapijson)). Set the `servers` URL to `http://localhost:5143/`.
6. **When the two disagree:** if this document and the running backend differ, the backend is a bug unless the difference is listed in §9. Report it and fix one side in the same week.
7. **Planned `/api/v2` endpoints ([§13](#13-planned-apiv2-endpoints-agreed-not-built))** are agreed before they are built. The PR that builds one moves it into §4–§7 unchanged. A PR that needs a different shape changes §13 first, with the frontend, and logs it in §13.12.

## 12. Appendix: TypeScript types

Copy this into the frontend (e.g. `src/api/contract.ts`). Keep it in sync with §6 and §7.

```ts
// ---- scalars ----
export type DateOnly = string;   // "yyyy-MM-dd"
export type TimeOnly = string;   // "HH:mm:ss" (send "HH:mm" or "HH:mm:ss")
export type Timestamp = string;  // UTC, ends in "Z" (contract 3.x could omit it)

// ---- enums ----
export type Role = "Admin" | "Doctor" | "Patient";
export type AppointmentStatus =
  | "Pending" | "Confirmed" | "Cancelled" | "Completed" | "NoShow"
  | "CheckedIn" | "InProgress" | "Rescheduled"; // the last three since 4.0.0-p0, not produced yet
export type SlotStatus = "Available" | "Booked" | "Blocked";
export type CancelledBy = "Patient" | "Doctor" | "Admin";
export type NotificationType =
  | "AppointmentReminder" | "AppointmentConfirmed" | "AppointmentCancelled"
  | "AppointmentCompleted" | "SystemAlert";
export type Gender = "Male" | "Female";
export type BloodType =
  | "APositive" | "ANegative" | "BPositive" | "BNegative"
  | "OPositive" | "ONegative" | "ABPositive" | "ABNegative";
export type DoctorSortBy = "Rating" | "Fee" | "Experience";
export type DayOfWeek = "Sunday" | "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday";

// ---- errors ----
export interface ProblemDetails {
  type?: string;
  title: string;            // error code for business errors, e.g. "Doctor.NotFound"; a reason phrase for shape E
  status: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>; // validation: PascalCase field names or error codes
}

// ---- auth ----
export interface RegisterPatientRequest {
  firstName: string; lastName: string; email: string; password: string; phoneNumber: string;
  gender: Gender; dateOfBirth?: DateOnly | null; bloodType?: BloodType | null;
  allergies?: string | null; chronicConditions?: string | null;
  emergencyContactName?: string | null; emergencyContactPhone?: string | null;
}
export interface RegisterDoctorRequest {
  firstName: string; lastName: string; email: string; password: string; phoneNumber: string;
  gender: Gender; dateOfBirth?: DateOnly | null; specialtyId: number; bio?: string | null;
  yearsOfExperience: number; consultationFee: number;
  clinicAddress?: string | null; clinicCity?: string | null;
}
export interface RegisterResponse { message: string; email: string; }
export interface LoginRequest { email: string; password: string; }
export interface LoginResponse {
  accessToken: string; refreshToken: string; expiresAt: Timestamp;
  email: string; fullName: string; role: Role;
}
export interface RefreshTokenRequest { refreshToken: string; }
export interface ConfirmEmailRequest { userId: string; token: string; }
export interface ForgotPasswordRequest { email: string; }
export interface ResetPasswordRequest { userId: string; token: string; newPassword: string; }

// ---- doctors ----
export interface DoctorSummaryResponse {
  id: number; fullName: string; specialtyName: string; yearsOfExperience: number;
  consultationFee: number; averageRating: number; clinicCity: string | null;
}
export interface DoctorResponse {
  id: number; fullName: string; specialtyName: string; bio: string | null;
  yearsOfExperience: number; consultationFee: number; averageRating: number; totalReviews: number;
  clinicAddress: string | null; clinicCity: string | null;
}
export interface UpdateDoctorProfileRequest {
  bio?: string | null; consultationFee?: number | null; clinicAddress?: string | null; clinicCity?: string | null;
}
export interface SlotResponse { id: number; date: DateOnly; startTime: TimeOnly; endTime: TimeOnly; status: SlotStatus; }
export interface ScheduleResponse {
  id: number; dayOfWeek: DayOfWeek; startTime: TimeOnly; endTime: TimeOnly;
  slotDurationMinutes: number; isActive: boolean;
}
export interface CreateScheduleRequest {
  dayOfWeek: DayOfWeek; startTime: TimeOnly; endTime: TimeOnly; slotDurationMinutes?: number;
}
export interface UpdateScheduleRequest {
  startTime?: TimeOnly; endTime?: TimeOnly; slotDurationMinutes?: number; isActive?: boolean;
}

// ---- appointments ----
export interface BookAppointmentRequest { slotId: number; reasonForVisit?: string | null; }
export interface CancelAppointmentRequest { cancellationReason?: string | null; }
export interface DoctorNotesRequest { notes?: string | null; }
export interface AppointmentResponse {
  id: number; slotId: number; doctorProfileId: number; patientProfileId: number;
  doctorName: string; patientName: string; appointmentDate: DateOnly;
  startTime: TimeOnly; endTime: TimeOnly; status: AppointmentStatus;
  reasonForVisit: string | null; doctorNotes: string | null; bookedAt: Timestamp;
  cancelledAt: Timestamp | null; cancellationReason: string | null;
  cancelledBy: CancelledBy | null; hasReview: boolean;
}

// ---- reviews ----
export interface SubmitReviewRequest { appointmentId: number; rating: number; comment?: string | null; }
export interface ReviewResponse {
  id: number; rating: number; comment: string | null; createdAt: Timestamp;
  isVisible: boolean; patientName: string;
}

// ---- profiles ----
export interface PatientProfileResponse {
  id: number; fullName: string; email: string; phoneNumber: string; bloodType: BloodType | null;
  allergies: string | null; chronicConditions: string | null;
  emergencyContactName: string | null; emergencyContactPhone: string | null;
}
export interface UpdatePatientProfileRequest {
  bloodType?: BloodType | null; allergies?: string | null; chronicConditions?: string | null;
  emergencyContactName?: string | null; emergencyContactPhone?: string | null;
}
export interface UserProfileResponse {
  id: string; firstName: string; lastName: string; email: string; phoneNumber: string | null;
  profilePictureUrl: string | null; gender: Gender; dateOfBirth: DateOnly | null;
  role: Role; createdAt: Timestamp;
}
export interface UpdateUserProfileRequest {
  firstName?: string | null; lastName?: string | null; phoneNumber?: string | null;
  profilePictureUrl?: string | null; gender?: Gender | null; dateOfBirth?: DateOnly | null;
}

// ---- notifications ----
export interface NotificationResponse {
  id: number; title: string; message: string; type: NotificationType; isRead: boolean;
  createdAt: Timestamp; relatedEntityId: number | null;
}

// ---- specialties & admin ----
export interface SpecialtyRequest {
  name: string; description?: string | null; iconUrl?: string | null;
  isActive?: boolean | null; // omit to keep the current state
}
export interface SpecialtyResponse {
  id: number; name: string; description: string | null; iconUrl: string | null; isActive: boolean;
}
export interface OverviewStatsResponse {
  totalDoctors: number; totalPatients: number; totalAppointments: number;
  totalSpecialties: number; pendingApprovals: number; totalReviews: number;
}
export interface AppointmentStatsResponse {
  pending: number; confirmed: number; completed: number; cancelled: number; noShow: number;
}

// ---- helpers ----
/** Total number of matching items on paged lists (#32, #38); null when the header is absent. */
export const readTotalCount = (response: Response): number | null => {
  const value = response.headers.get("X-Total-Count");
  return value === null ? null : Number(value);
};

/** Timestamps from the API are UTC with the "Z" suffix; a value without one (contract 3.x) is read as UTC too. */
export const parseTimestamp = (value: Timestamp): Date =>
  new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`);
```

## 13. Planned `/api/v2` endpoints (agreed, not built)

**Nothing in this section exists in the API yet.** It records the `/api/v2` endpoints the V2 frontend already uses through its mock backend, agreed on 2026-10-27 so that both sides build against one fixed shape. The frontend writes its HTTP adapters against this section; the backend builds these endpoints to match it.

- **Source.** The shapes come from the frontend's service interfaces (`src/features/*/api/*.service.ts` and `model/*.ts` on the frontend's `v2`, commit `16a6dcb`). Where they differed from [07 §4](v2/07_API_DESIGN.md#4-key-endpoints-per-module), the frontend's shape was taken when it was richer (P1 plan Q-A). Where it clashed with a backend rule, the rule won, and [§13.11](#1311-frontend-changes-this-section-implies) lists the frontend changes that follow.
- **Building an endpoint** moves it from here into §4–§7 **unchanged**, with a changelog entry. A backend PR that needs a different shape changes this section first, agrees it with the frontend, and records the change in [§13.12](#1312-revisions-of-this-section). A difference between this section and a built endpoint is a backend bug, as in [§11](#11-how-to-change-this-contract) rule 6.
- **Phase** says which backend phase builds each endpoint ([10_ROADMAP.md](v2/10_ROADMAP.md)). Until then the frontend keeps its mock behind the same interface.
- **Staff screens:** the front desk and nurse station (patients, the desk's day, the queue board and waiting-room screen, triage, checkout and cash sessions) are in [§13.13–§13.21](#1313-front-desk-and-nurse-station-conventions), added once the frontend built them.
- **Not covered yet:** the doctor's screens, recruitment review, and organization, staff and catalog administration. The frontend hasn't built them; [07 §4](v2/07_API_DESIGN.md#4-key-endpoints-per-module) is the direction for them until it does, and they are added here then.

### 13.1 Conventions for every `/api/v2` route

These replace the matching rules of §1–§3 for `/api/v2` routes only. The inherited unversioned routes keep §1–§3.

| Topic | Rule |
|---|---|
| Ids | **Every id is a JSON string** (`"42"`, or a GUID for users), in requests, responses and paths. Treat ids as opaque: never parse them or compare them as numbers. (The database keys stay integers; V2 responses send them as strings.) |
| Phone numbers | Field name `phone`. Requests accept an Egyptian mobile as `01xxxxxxxxx` or `+201xxxxxxxxx` (`01` then `0`, `1`, `2` or `5`, then 8 digits). Responses always send E.164 (`+201…`). Anything else is a 400 validation error on the field |
| Language | `"ar"` or `"en"`. Arabic is the default |
| Dates and times | Branch wall clock: `date` is `"yyyy-MM-dd"`, `startTime` / `endTime` are **`"HH:mm"`** (no seconds, unlike §1). Requests accept `"HH:mm"` or `"HH:mm:ss"`. Don't convert them to another zone ([ADR 0003](v2/adr/0003-branch-time-zones.md)) |
| Instants | UTC ISO 8601 ending in `Z`. Fields are named `…At` or `…AtUtc` |
| Money | JSON number, EGP, 2 decimals |
| Enums | PascalCase strings in responses and bodies. **Query-string enum values are case-insensitive** (`sort=soonest` = `sort=Soonest`). New values are additive, so show an unknown value instead of failing |
| Bilingual reference data | `nameEn` and `nameAr` on branches, specialties and services. Text people type (bios, notes) is stored as written, with `…En` / `…Ar` pairs where both exist |
| Lists | A bare JSON array, with the total of matching items in `X-Total-Count`, on **every** list marked *paged*. `pageNumber` (1-based) and `pageSize`: defaults 1 and 20, `pageSize` at most 100; out of range is 400. Lists marked *full* (small reference data) return every item and no header |
| Errors with a code | **Always ProblemDetails with `title` = the code** (shape A), whatever the status, 400 included. Shape C (a code as a key of `errors`) is not used on `/api/v2` |
| Validation errors | Shape B, with **camelCase** keys that are property paths (`phone`, `lines[0].unitPrice`) |
| Error extensions | Some errors carry extra members at the top level of the ProblemDetails: `attemptsLeft` (number; a wrong one-time code) and `retryAt` (UTC instant; lockouts, cooldowns and limits). They are listed with each code in [§13.10](#1310-error-codes) |
| 401 | **Only means the token is the problem**: no access token, or an invalid or expired one (shape E), and on `/auth/refresh-token` and `/auth/logout` a bad refresh token. A wrong password, code or challenge is **400**; a lockout, cooldown or limit is **429** with `retryAt`; an account that may not sign in is **403**. (The frontend's client refreshes and retries on every 401, so a 401 for a wrong password would send it into a refresh.) This differs from §2.2, where those are 401 |
| Rate limits | The credential and code endpoints are rate-limited per client IP as in [§2.7](#27-rate-limiting); the one-time code endpoints also have the `Otp` limits (per number and per IP). Over a limit: 429 `RateLimit.Exceeded` with `retryAt` and `Retry-After` |
| Tokens from links | Invitation, reminder, email-confirmation and password-reset tokens travel **in the request body**, never in an API path or query string, so they stay out of server and proxy logs. (The frontend's own hash routes, such as `/#/r/{token}`, never reach the server) |
| Idempotency | `Idempotency-Key: <uuid>` is **required** on `POST /appointments`, `POST /appointments/{id}/deposit`, `POST /payments/online-checkout`, `POST /appointments/book-on-behalf`, `POST /appointments/walk-in` and `POST /appointments/{id}/payments`. A repeat with the same key and body within 24 h returns the first response again; the same key with a different body is 409 `Idempotency.KeyReused`; a missing key is 400 |
| Files | Uploads are `multipart/form-data`. Downloads stream from an access-checked endpoint as `Content-Disposition: attachment`, never from a public URL. A file whose virus scan hasn't passed yet returns 409 `File.NotScanned` |
| Sessions | Login, MFA, sign-up verification, invitation acceptance and refresh all return a `Session` ([§13.2](#132-shared-types)): the tokens of §2.1 plus `user` (the `GET /me` body). Refresh and logout keep the rules of §2.3 and §2.6 |

### 13.2 Shared types

```ts
// ---- scalars ----
export type Id = string;          // opaque; integers on the server, strings on the wire
export type Phone = string;       // E.164 in responses: "+201001234567"
export type Language = "ar" | "en";
export type DateOnly = string;    // "yyyy-MM-dd", branch wall clock
export type TimeOfDay = string;   // "HH:mm", branch wall clock
export type Instant = string;     // UTC, ends in "Z"

// ---- access (docs/v2/02_ROLES_AND_PERMISSIONS.md §3–§6) ----
export type Role = "Admin" | "Manager" | "Receptionist" | "Nurse" | "Doctor" | "Accountant" | "Patient";
export type Scope = "Own" | "Branch" | "Organization";
export type Permission = string;  // one of the 59 codes of 02 §5 (Shared/Authorization/Permissions.cs), e.g. "appointments.check_in"
export interface PermissionGrant { permission: Permission; scope: Scope; branchIds?: Id[] }   // branchIds when scope is "Branch"
export interface RoleAssignment { role: Role; scope: "Organization" | "Branch"; branchIds?: Id[] }
export type NotificationChannel = "Phone" | "Email" | "Both";  // Phone = WhatsApp, or SMS when it is on

/** GET /api/v2/me */
export interface Me {
  id: Id;                        // the user's GUID
  fullName: string;
  phone: Phone;
  email?: string;
  preferredLanguage: Language;
  notificationChannel: NotificationChannel;
  roles: RoleAssignment[];
  permissions: PermissionGrant[];   // what menus and actions are built from (ADR 0002)
  phoneVerified: boolean;
  emailConfirmed: boolean;
  hasDoctorApplication: boolean;    // an applicant has no role but an application (02 §2)
  mfaEnabled: boolean;              // two-step sign-in is required for this account (staff, FR-STF-07)
  mfaEnrolled: boolean;             // an authenticator is set up
  isMedicalDirector: boolean;
}

// ---- sessions ----
export interface Session { accessToken: string; refreshToken: string; expiresAt: Instant; user: Me }
export interface MfaChallenge { id: Id; identifier: string; roleLabel: Role }   // shown on the second step
export type SignInResult =
  | ({ status: "SignedIn" } & Session)
  | { status: "MfaRequired"; challenge: MfaChallenge };
export type OtpPurpose = "SignUp" | "PasswordReset" | "ChangePhone";
export interface OtpTicket { phone: Phone; expiresAt: Instant; resendAt: Instant }  // resendAt: 60 s after sending
export interface MfaEnrollment { secret: string; otpauthUri: string; recoveryCodes: string[] }
export interface Invitation {
  email: string; fullName: string; roles: RoleAssignment[]; invitedByName: string; expiresAt: Instant;
}

// ---- organization and catalog ----
export interface Branch {
  id: Id; code: string; nameEn: string; nameAr: string; addressEn: string; addressAr: string;
  phone: Phone; timeZone: string; /* IANA */ isActive: boolean;
}
export interface Specialty { id: Id; nameEn: string; nameAr: string }
export type ServiceCategory = "Consultation" | "FollowUp" | "Procedure" | "Package";
export interface Service {
  id: Id; code: string; nameEn: string; nameAr: string; category: ServiceCategory; specialtyId: Id;
  defaultDurationMinutes: number;
  optionalDepositAmount?: number;   // never required to book (FR-APT-08)
}
export interface BranchService extends Service { fromPrice: number; doctorCount: number }

// ---- doctors (public) ----
export interface Slot {
  id: Id; doctorId: Id; branchId: Id; date: DateOnly; startTime: TimeOfDay; endTime: TimeOfDay;
  startsAtUtc: Instant; available: boolean;
}
export interface WeeklyShift { branchId: Id; weekday: number; /* 0 = Sunday … 6 = Saturday */ startTime: TimeOfDay; endTime: TimeOfDay }
export interface DoctorServiceOffer {
  serviceId: Id; durationMinutes: number; prices: { branchId: Id; price: number }[]; depositAmount?: number;
}
export interface DoctorSummary {
  id: Id; fullName: string; specialtyId: Id; branchIds: Id[];
  languages: Language[]; yearsOfExperience: number; rating: number; reviewCount: number;
  fromPrice: number | null;   // lowest consultation price; at the searched branch when one is given
  nextSlots: Slot[];          // up to 3
}
export interface DoctorProfile extends DoctorSummary {
  licenseNo: string; bioEn?: string; bioAr?: string;
  services: DoctorServiceOffer[]; schedule: WeeklyShift[];
  ratingCounts: { "1": number; "2": number; "3": number; "4": number; "5": number };
}
export interface Review { id: Id; authorName: string; /* "Tarek M." */ rating: number; comment?: string; createdAt: Instant }

// ---- doctor applications ----
export type ApplicationStatus = "Applied" | "Interview" | "Accepted" | "Rejected" | "Withdrawn";
export interface StoredFileInfo { id: Id; fileName: string; sizeBytes: number; contentType: string }
export interface InterviewDetails { at: Instant; branchId?: Id; location?: string; interviewerName: string; notes?: string }
export interface AcceptanceDetails { startDate: DateOnly; branchId: Id; notes?: string }
export interface StageChange {
  id: Id; fromStatus: ApplicationStatus | null; toStatus: ApplicationStatus; changedAt: Instant;
  interview?: InterviewDetails; acceptance?: AcceptanceDetails; message?: string; emailSent: boolean;
}
export interface DoctorApplication {
  id: Id; reference: string; status: ApplicationStatus; applicantName: string; phone: Phone; email: string;
  specialtyId: Id; licenseNo: string; yearsOfExperience: number; preferredBranchId?: Id; submittedAt: Instant;
  cv: StoredFileInfo; coverLetter?: StoredFileInfo; coverLetterText?: string;
  interview?: InterviewDetails; acceptance?: AcceptanceDetails;
  history: StageChange[];        // newest first
  canReapplyFrom?: DateOnly;     // after a rejection or withdrawal
}

// ---- appointments (the patient's view) ----
export type AppointmentStatus =
  | "Confirmed" | "CheckedIn" | "InProgress" | "Completed" | "Cancelled" | "Rescheduled" | "NoShow";
  // "Pending" is never sent on /api/v2
export type BookingSource = "Online" | "Phone" | "Desk" | "WalkIn";
export type CancellationReasonCode = "ScheduleConflict" | "FeelingBetter" | "BookedElsewhere" | "CostOrInsurance" | "Other";
export type QueueStage = "WaitingTriage" | "ReadyForDoctor" | "WithDoctor" | "Done";
export interface QueueStatus {
  ticketNo: string; stage: QueueStage; position: number; estimatedWaitMinutes: number;
  roomName?: string; doctorDelayMinutes?: number; checkedInAt?: Instant; triagedAt?: Instant; updatedAt: Instant;
}
export interface Appointment {
  id: Id; reference: string; /* "CF-26-08731" */ status: AppointmentStatus; source: BookingSource;
  doctorId: Id; doctorName: string; specialtyId: Id; branchId: Id; serviceId: Id;
  date: DateOnly; startTime: TimeOfDay; endTime: TimeOfDay; startsAtUtc: Instant;
  price: number; depositAmount?: number; depositPaid: boolean; bookedAt: Instant;
  changeableUntil: Instant;      // the patient may cancel or reschedule online until then (FR-ORG-05)
  cancelledAt?: Instant; cancelledBy?: "Patient" | "Clinic"; cancellationReason?: CancellationReasonCode;
  rescheduledToId?: Id; rescheduledFromId?: Id;
  attendanceConfirmed: boolean; checkedInAt?: Instant; completedAt?: Instant;
  visitId?: Id; hasReview: boolean; queue?: QueueStatus;
}
export interface BookRequest { doctorId: Id; branchId: Id; serviceId: Id; slotId: Id; acceptTerms: boolean }
export interface CancelRequest { reason: CancellationReasonCode; note?: string }
export interface ReminderView {
  appointmentId: Id; reference: string; status: AppointmentStatus; attendanceConfirmed: boolean;
  doctorName: string; specialtyId: Id; branchId: Id; serviceId: Id;
  date: DateOnly; startTime: TimeOfDay; startsAtUtc: Instant; changeableUntil: Instant; price: number;
}

// ---- visits (patient-facing summary, FR-EMR-13) ----
export interface Prescription { drug: string; strength?: string; genericName?: string; dosage: string; durationDays?: number; instructions?: string }
export interface VisitOrder { name: string; detail?: string; status: "Pending" | "ResultReceived"; resultNote?: string }
export interface FollowUp { from: DateOnly; to: DateOnly; serviceId?: Id }
export interface VisitSummary {
  id: Id; appointmentId: Id; appointmentReference: string; startsAtUtc: Instant;
  doctorId: Id; doctorName: string; specialtyId: Id; branchId: Id;
  diagnoses: string[]; findings?: string; prescriptions: Prescription[]; prescriptionCode?: string;
  orders: VisitOrder[]; instructions: string[]; followUp?: FollowUp;
}
export interface VisitListItem {
  id: Id; startsAtUtc: Instant; doctorId: Id; doctorName: string; specialtyId: Id; branchId: Id;
  diagnoses: string[]; followUp?: FollowUp; prescriptionCount: number; orderCount: number;
}

// ---- billing (the patient's view) ----
export type InvoiceStatus = "Issued" | "PartiallyPaid" | "Paid" | "Void";
export type PaymentMethod = "Cash" | "Card" | "Online" | "Wallet" | "BankTransfer";
export interface PatientInvoice {
  id: Id; number: string; /* "<BRANCH>-<YYYY>-<000001>" */ branchId: Id; doctorName?: string; issuedAt: Instant;
  status: InvoiceStatus; lines: { description: string; amount: number }[]; total: number;
  deductions: { kind: "Deposit" | "Insurance" | "Discount"; label?: string; amount: number }[];
  payments: { method: PaymentMethod; amount: number; paidAt: Instant }[];
  balanceDue: number;
}

// ---- documents and insurance ----
export interface PatientDocument {
  id: Id; fileName: string; contentType: string; sizeBytes: number; uploadedAt: Instant; sharedWithDoctorName?: string;
}
export type PolicyStatus = "Verified" | "PendingVerification" | "Expired";
export interface InsurancePolicy {
  id: Id; payerName: string; planName?: string; memberNo: string; holderName: string;
  coveragePercent?: number; validTo: DateOnly; isDefault: boolean; status: PolicyStatus;
}
export interface Payer { id: Id; name: string }

// ---- profile and notifications ----
export interface PatientProfile {
  fullName: string; mrn: string; memberSince: DateOnly; phone: Phone; phoneVerified: boolean;
  email?: string; emailConfirmed: boolean; notificationChannel: NotificationChannel; preferredLanguage: Language;
  bloodType?: string; allergies: { name: string; severity?: "Mild" | "Moderate" | "Severe" }[]; conditions: string[];
}
export type NotificationType =
  | "AppointmentConfirmed" | "AppointmentReminder" | "AppointmentRescheduled" | "AppointmentCancelled"
  | "DepositReceived" | "InvoiceIssued" | "PaymentReceived" | "PrescriptionReady";
  // staff notification types are added with the staff screens (additive)
export type NotificationCategory = "Appointments" | "Billing" | "Prescriptions";
export interface AppNotification {
  id: Id; type: NotificationType; title: string; message: string;   // rendered in the recipient's language
  createdAt: Instant; isRead: boolean;
  link?: { kind: "Appointment" | "Invoice" | "Visit"; id: Id };
}
```

Optional fields (`?`) are omitted from responses when they have no value.

### 13.3 Auth and sessions

Auth column: **—** anonymous; **Signed in** any valid token.

| Method and path | Auth | Body | Success | Errors | Phase |
|---|---|---|---|---|---|
| `POST /api/v2/auth/register` | — | `{ firstName, lastName, phone, email?, password, preferredLanguage }` | 200 `OtpTicket`; a `SignUp` code is sent. The account can't sign in until it is verified | 400 validation; 409 `Auth.EmailAlreadyExists`; 429 `Otp.ResendTooSoon`, `Otp.TooManyAttempts`, `RateLimit.Exceeded`; 503 `Messaging.PhoneUnreachable` | P1 |
| `POST /api/v2/auth/register/verify` | — | `{ phone, code }` | 200 `Session` | 400 `Otp.InvalidCode` (+`attemptsLeft`); 429 `Otp.TooManyAttempts` (+`retryAt`) | P1 |
| `POST /api/v2/auth/otp/send` | — (`ChangePhone`: signed in) | `{ phone, purpose }` | 200 `OtpTicket` (a new code). **The same answer for a number with no account**, and then nothing is sent (08 §2) | 400 validation; 429 `Otp.ResendTooSoon`, `Otp.TooManyAttempts` (+`retryAt`); 503 `Messaging.PhoneUnreachable` | P1 |
| `POST /api/v2/auth/login` | — | `{ identifier, password }`: a phone (either form of §13.1) or an email | 200 `SignInResult`. Staff with an authenticator get `MfaRequired`; everyone else `SignedIn` | 400 `Auth.InvalidCredentials` (also for an unknown account); 403 `Auth.AccountNotActive`, `Auth.PhoneNotVerified`; 429 `Auth.AccountLockedOut` (+`retryAt`), `RateLimit.Exceeded` | P1 |
| `POST /api/v2/auth/mfa` | — | `{ challengeId, code }` or `{ challengeId, recoveryCode }` | 200 `Session`. A recovery code works once | 400 `Auth.InvalidMfaCode`, `Auth.MfaChallengeExpired` (a challenge lasts 5 min); 429 `Auth.AccountLockedOut` (+`retryAt`) | P1 |
| `POST /api/v2/auth/mfa/enroll` | Signed in (staff) | — | 200 `MfaEnrollment`. The recovery codes become valid on confirmation | — | P1 |
| `POST /api/v2/auth/mfa/confirm` | Signed in (staff) | `{ code }` | 200 `Me` (`mfaEnrolled: true`) | 400 `Auth.InvalidMfaCode` | P1 |
| `POST /api/v2/auth/refresh-token` | — | `{ refreshToken }` | 200 `Session` | 401 `Auth.InvalidRefreshToken` | P1 |
| `POST /api/v2/auth/logout` | — (token optional) | `{ refreshToken }` | 204 | 401 `Auth.InvalidRefreshToken` | P1 |
| `POST /api/v2/auth/password/forgot` | — | `{ email }` | 204 whether or not the email has an account; the email links to `/#/reset-password` ([§13.9](#139-frontend-routes-the-backend-will-link-to)) | 429 `RateLimit.Exceeded` | P1 |
| `POST /api/v2/auth/password/reset` | — | `{ userId, token, newPassword }` | 204; every session is revoked | 400 validation, `Auth.InvalidPasswordResetToken` (also for an unknown or deactivated user) | P1 |
| `POST /api/v2/auth/password/check-code` | — | `{ phone, code }` | 204. Checks a `PasswordReset` code **without using it up**; a wrong try still counts | 400 `Otp.InvalidCode` (+`attemptsLeft`; also for a number with no account); 429 `Otp.TooManyAttempts` (+`retryAt`) | P1 |
| `POST /api/v2/auth/password/reset-by-otp` | — | `{ phone, code, newPassword }` | 204; uses the code up and revokes every session | as `check-code`, plus 400 validation | P1 |
| `POST /api/v2/auth/confirm-email` | — | `{ userId, token }` | 204 | 400 `Auth.InvalidEmailConfirmationToken` (also for an unknown user) | P1 |
| `POST /api/v2/auth/invitations/preview` | — | `{ token }` | 200 `Invitation` | 404 `Invitation.NotFound` (unknown, used or revoked); 409 `Invitation.Expired` | P1 |
| `POST /api/v2/auth/invitations/accept` | — | `{ token, fullName, phone, password }` | 200 `Session`; the staff member is active, and two-step setup comes next | as `preview`, plus 400 validation; 409 `Auth.PhoneAlreadyUsed` | P1 |

**Staff before MFA.** A staff member who hasn't set up an authenticator signs in with `SignedIn` and `mfaEnrolled: false`. Until they confirm one, that token reaches only `GET /me`, `/auth/mfa/enroll`, `/auth/mfa/confirm` and `/auth/logout`; every other route, inherited ones included, returns 403 `Auth.MfaEnrollmentRequired`. In Development with demo data, `Auth:RequireStaffMfa=false` turns this off (P1 plan Q-B), and `mfaEnabled` is then `false`.

### 13.4 Me and the patient's profile

| Method and path | Auth | Body | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/me` | Signed in | — | 200 `Me` | — | P1 |
| `PATCH /api/v2/me` | Signed in | `{ preferredLanguage?, notificationChannel? }` | 200 `Me` | 400 validation, `Profile.EmailRequired` (`Email` or `Both` without a confirmed email) | P1 |
| `POST /api/v2/me/phone` | Signed in | `{ phone }` | 200 `OtpTicket`; a `ChangePhone` code goes to the new number | 400 validation; 409 `Auth.PhoneAlreadyUsed`; 429 `Otp.ResendTooSoon`, `Otp.TooManyAttempts` | P1 |
| `POST /api/v2/me/phone/confirm` | Signed in | `{ phone, code }` | 200 `Me`; the new number replaces the old one | 400 `Otp.InvalidCode` (+`attemptsLeft`); 409 `Auth.PhoneAlreadyUsed`; 429 `Otp.TooManyAttempts` | P1 |
| `POST /api/v2/me/email` | Signed in | `{ email }` | 204; a confirmation link is emailed, and the email is used once it is opened | 400 validation; 409 `Auth.EmailAlreadyExists` | P1 |
| `POST /api/v2/me/password` | Signed in | `{ currentPassword, newPassword }` | 204; every other session is revoked | 400 validation, `Auth.InvalidCredentials` (wrong current password: **400**, not 401) | P1 |
| `GET /api/v2/me/patient-profile` | Patient | — | 200 `PatientProfile` | 404 `Patient.ProfileNotFound` | P1 |

### 13.5 Public reference data and the doctor directory

All anonymous.

| Method and path | Query | Success | Errors | Phase |
|---|---|---|---|---|
| `GET /api/v2/public/branches` | — | 200 `Branch[]`, *full*, active branches only | — | P1 |
| `GET /api/v2/public/specialties` | — | 200 `Specialty[]`, *full*, active only | — | P1 |
| `GET /api/v2/public/services` | — | 200 `Service[]`, *full*, active only | — | P1 |
| `GET /api/v2/public/branches/{branchId}/services` | — | 200 `BranchService[]`, *full*: services at least one public doctor offers at the branch, with the lowest price there | 404 `Branch.NotFound` | P2 |
| `GET /api/v2/public/doctors` | `text?` (name or specialty, either language), `branchId?`, `specialtyId?`, `serviceId?` (then `fromPrice` is that service's price), `availability?` (`Today`, `Tomorrow`, `Week`), `language?`, `sort?` (`Soonest`, `Rating`, `Price`; default `Soonest`), paging | 200 `DoctorSummary[]`, *paged* | 400 validation | P2 |
| `GET /api/v2/public/doctors/{doctorId}` | — | 200 `DoctorProfile` | 404 `Doctor.NotFound` (also for a hidden doctor) | P2 |
| `GET /api/v2/public/doctors/{doctorId}/slots` | `branchId` (required), `from?` (branch date; default today there), `days` (1–14) | 200 `Slot[]`, *full*, in time order | 400 validation; 404 `Doctor.NotFound`, `Branch.NotFound` | P2 |
| `GET /api/v2/public/doctors/{doctorId}/reviews` | `rating?` (1–5), paging | 200 `Review[]`, *paged*, newest first | 404 `Doctor.NotFound` | P2 |
| `GET /api/v2/public/payers` | — | 200 `Payer[]`, *full*, active payers | — | P5 |

### 13.6 Doctor applications (the applicant's side)

For a signed-in user without a staff role. Applying needs a verified phone and a confirmed email.

| Method and path | Body | Success | Errors | Phase |
|---|---|---|---|---|
| `POST /api/v2/doctor-applications` | `multipart/form-data`: `specialtyId`, `licenseNo`, `yearsOfExperience`, `preferredBranchId?`, `cv` (file, required), `coverLetter?` (file) or `coverLetterText?`. Files: PDF or DOCX, at most 5 MB each, checked by content | 201 `DoctorApplication` | 400 validation, `DoctorApplication.CvRequired`, `File.TypeNotAllowed`, `File.TooLarge`; 403 `Auth.PhoneNotVerified`, `Auth.EmailNotConfirmed`; 409 `DoctorApplication.AlreadyOpen`, `DoctorApplication.ReapplyTooSoon` | P1 |
| `GET /api/v2/doctor-applications/mine` | — | 200 `DoctorApplication` (the latest), or **204** when the user never applied | — | P1 |
| `POST /api/v2/doctor-applications/mine/withdraw` | — | 200 `DoctorApplication` (`Withdrawn`) | 404 `DoctorApplication.NotFound`; 409 `DoctorApplication.InvalidStatusTransition` (not `Applied` or `Interview`) | P1 |
| `GET /api/v2/doctor-applications/mine/files/{fileId}` | — | 200, the file (attachment) | 404 `File.NotFound` (also another applicant's file); 409 `File.NotScanned` | P1 |

### 13.7 The patient's appointments, queue and reminders

| Method and path | Auth | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/me/appointments` | Patient | `status?`, paging | 200 `Appointment[]`, *paged*, newest `startsAtUtc` first | 400 validation | P2 |
| `GET /api/v2/appointments/{appointmentId}` | Patient (own) | — | 200 `Appointment` | 403 `Appointment.Unauthorized`; 404 `Appointment.NotFound` | P2 |
| `POST /api/v2/appointments` | Patient; `Idempotency-Key` | `BookRequest` | 201 `Appointment`, always `Confirmed`, deposit or not (FR-APT-02, FR-APT-08) | 400 validation, `Consent.Required` (`acceptTerms` not `true`), `Appointment.SlotInPast`, `Booking.ServiceNotOffered` (the doctor doesn't offer the service at that branch); 403 `Auth.PhoneNotVerified`; 404 `Appointment.SlotNotFound`; 409 `Slot.CapacityReached` (taken meanwhile), `Idempotency.KeyReused` | P2 |
| `POST /api/v2/appointments/{appointmentId}/reschedule` | Patient (own) | `{ slotId }` | 200 the **new** `Appointment`; the old one becomes `Rescheduled` with `rescheduledToId` | 400 `Appointment.CancellationWindowExpired`, `Appointment.SlotInPast`; 403, 404 as above; 409 `Slot.CapacityReached`, `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/appointments/{appointmentId}/cancel` | Patient (own) | `CancelRequest` | 200 `Appointment` (`Cancelled`) | 400 validation, `Appointment.CancellationWindowExpired`; 403, 404 as above; 409 `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/appointments/{appointmentId}/review` | Patient (own) | `{ rating, comment? }` (1–5; comment ≤ 1000) | 204 | 400 validation, `Appointment.ReviewNotAllowed` (not `Completed`); 403, 404 as above; 409 `Appointment.ReviewAlreadyExists` | P2 |
| `POST /api/v2/appointments/{appointmentId}/deposit` | Patient (own); `Idempotency-Key` | — | 200 `{ checkoutUrl }` (Paymob hosted checkout) | 403, 404 as above; 409 `Deposit.NotOffered`, `Deposit.AlreadyPaid`, `Appointment.InvalidStatusTransition`; 503 `Payment.GatewayUnavailable` (the booking is unaffected) | P3 |
| `GET /api/v2/appointments/{appointmentId}/queue` | Patient (own) | — | 200 `QueueStatus`, or 204 when not checked in | 403, 404 as above | P2 |
| `POST /api/v2/public/reminders/view` | — | `{ token }` | 200 `ReminderView` | 404 `Reminder.LinkInvalid` (unknown or expired) | P2 |
| `POST /api/v2/public/reminders/confirm` | — | `{ token }` | 200 `ReminderView` (`attendanceConfirmed: true`) | 404 `Reminder.LinkInvalid`; 409 `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/public/reminders/cancel` | — | `{ token }` | 200 `ReminderView` (`Cancelled`, reason `Other`) | 400 `Appointment.CancellationWindowExpired`; 404 `Reminder.LinkInvalid`; 409 `Appointment.InvalidStatusTransition` | P2 |

**Live queue (P2).** SignalR hub `/hubs/queue`, with the access token as SignalR sends it (`access_token` query parameter on the WebSocket). The patient calls `WatchAppointment(appointmentId)` and receives `QueueStatusChanged(status: QueueStatus)` at once and on every change; `UnwatchAppointment(appointmentId)` stops it. Watching another patient's appointment fails with the hub error `Appointment.Unauthorized`. Staff use the same hub for the queue board (added with the front desk).

### 13.8 Visits, invoices, documents, insurance and notifications

| Method and path | Auth | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/me/visits` | Patient | paging | 200 `VisitListItem[]`, *paged*, newest first; published summaries only | 400 validation | P4 |
| `GET /api/v2/me/visits/{visitId}` | Patient (own) | — | 200 `VisitSummary` | 404 `Visit.NotFound` (also another patient's) | P4 |
| `GET /api/v2/me/visits/{visitId}/prescription` | Patient (own) | — | 200 PDF (attachment) | 404 `Visit.NotFound`, `Visit.NoPrescription` | P4 |
| `GET /api/v2/me/invoices` | Patient | `status?`, paging | 200 `PatientInvoice[]`, *paged*, newest first | 400 validation | P3 |
| `GET /api/v2/me/invoices/{invoiceId}/pdf` | Patient (own) | — | 200 PDF (the invoice, or the receipt once paid). `Accept-Language` picks its language | 404 `Invoice.NotFound` | P3 |
| `POST /api/v2/payments/online-checkout` | Patient (own); `Idempotency-Key` | `{ invoiceId }` | 200 `{ checkoutUrl }` for the balance due | 404 `Invoice.NotFound`; 409 `Invoice.NothingDue`, `Idempotency.KeyReused`; 503 `Payment.GatewayUnavailable` | P3 |
| `GET /api/v2/me/documents` | Patient | paging | 200 `PatientDocument[]`, *paged*, newest first | 400 validation | P4 |
| `POST /api/v2/me/documents` | Patient | `multipart/form-data`: `file` (PDF, JPEG or PNG, at most 20 MB, checked by content) | 201 `PatientDocument`. It is scanned in the background; until then a download is 409 `File.NotScanned` | 400 `File.TypeNotAllowed`, `File.TooLarge` | P4 |
| `GET /api/v2/me/documents/{documentId}/content` | Patient (own) | — | 200, the file (attachment) | 404 `File.NotFound`; 409 `File.NotScanned` | P4 |
| `DELETE /api/v2/me/documents/{documentId}` | Patient (own) | — | 204 | 404 `File.NotFound`; 409 `Document.SharedWithDoctor` (attached to a visit) | P4 |
| `GET /api/v2/me/policies` | Patient | — | 200 `InsurancePolicy[]`, *full* | — | P5 |
| `POST /api/v2/me/policies` | Patient | `multipart/form-data`: `payerId`, `memberNo`, `holderName`, `validTo`, `cardFront` (JPEG or PNG), `cardBack?` | 201 `InsurancePolicy` (`PendingVerification`; the desk verifies it) | 400 validation, `File.TypeNotAllowed`, `File.TooLarge`; 404 `Payer.NotFound` | P5 |
| `GET /api/v2/notifications` | Signed in | `category?`, paging | 200 `AppNotification[]`, *paged*, newest first | 400 validation | P2 |
| `GET /api/v2/notifications/unread-count` | Signed in | — | 200 `{ "count": number }` | — | P2 |
| `POST /api/v2/notifications/{notificationId}/read` | Signed in (own) | — | 204 | 403 `Notification.Unauthorized`; 404 `Notification.NotFound` | P2 |
| `POST /api/v2/notifications/read-all` | Signed in | — | 204 | — | P2 |

Notifications still arrive about 2 s after the action that causes them ([§1](#1-general-conventions)); `/hubs/notifications` (P2) can replace polling later.

### 13.9 Frontend routes the backend will link to

Like [§8](#8-frontend-routes-the-backend-depends-on). The frontend reads the token from the link and sends it in a request body ([§13.1](#131-conventions-for-every-apiv2-route)).

| Route | Sent in | Then |
|---|---|---|
| `/#/confirm-email?userId=…&token=…` | Email confirmation (sign-up with an email, `POST /me/email`) | `POST /api/v2/auth/confirm-email` |
| `/#/reset-password?userId=…&token=…` | `POST /auth/password/forgot` | `POST /api/v2/auth/password/reset`. **`userId`, not the email**, as in §8, so no address sits in the URL |
| `/#/invitation/{token}` | Staff invitation email | `POST /api/v2/auth/invitations/preview`, then `/accept` |
| `/#/r/{token}` | Reminder message (WhatsApp, SMS or email) | `POST /api/v2/public/reminders/view`, `/confirm`, `/cancel` |

### 13.10 Error codes

New codes, and existing codes whose status differs on `/api/v2`. Codes not listed here keep §3.3's status, always as shape A.

| Code | Status | Extensions | When |
|---|---|---|---|
| `Auth.InvalidCredentials` | **400** | — | Wrong identifier or password (also an unknown account); wrong current password on `POST /me/password` |
| `Auth.AccountLockedOut` | **429** | `retryAt` | Locked after repeated failures |
| `Auth.AccountNotActive` | **403** | — | Deactivated account |
| `Auth.EmailNotConfirmed` | **403** | — | Applying without a confirmed email |
| `Auth.PhoneNotVerified` | 403 | — | Signing in, booking or applying before the phone is verified |
| `Auth.PhoneAlreadyUsed` | 409 | — | The number belongs to another account |
| `Auth.EmailAlreadyExists` | 409 | — | The email belongs to another account |
| `Auth.InvalidMfaCode` | **400** | — | Wrong authenticator or recovery code |
| `Auth.MfaChallengeExpired` | 400 | — | The challenge is older than 5 minutes or already used; sign in again |
| `Auth.MfaEnrollmentRequired` | 403 | — | A staff token without an authenticator, on any route except those named in §13.3 |
| `Auth.InvalidPasswordResetToken`, `Auth.InvalidEmailConfirmationToken` | 400 | — | Invalid or expired link token, or an unknown user |
| `Otp.InvalidCode` | 400 | `attemptsLeft` | Wrong or expired code (also for a number with no account) |
| `Otp.TooManyAttempts` | 429 | `retryAt` | 5 wrong tries on a code, or a daily cap reached |
| `Otp.ResendTooSoon` | 429 | `retryAt` | A new code within 60 s of the last one |
| `Messaging.PhoneUnreachable` | 503 | — | No live phone channel can reach the number (no WhatsApp and SMS off) |
| `Invitation.NotFound` | 404 | — | Unknown, used or revoked invitation |
| `Invitation.Expired` | 409 | — | Older than 72 h |
| `Profile.EmailRequired` | 400 | — | Choosing `Email` or `Both` without a confirmed email |
| `Branch.NotFound` | 404 | — | Unknown or inactive branch |
| `Doctor.NotFound` | 404 | — | Unknown or hidden doctor |
| `DoctorApplication.NotFound` | 404 | — | The user has no application |
| `DoctorApplication.AlreadyOpen` | 409 | — | An `Applied` or `Interview` application exists |
| `DoctorApplication.ReapplyTooSoon` | 409 | `retryAt` | Within the cool-down after a rejection or withdrawal |
| `DoctorApplication.CvRequired` | 400 | — | No CV attached |
| `DoctorApplication.InvalidStatusTransition` | 409 | — | The stage move isn't allowed from the current status |
| `File.TypeNotAllowed`, `File.TooLarge` | 400 | — | The upload fails the content-type or size check |
| `File.NotFound` | 404 | — | Unknown file, or not the caller's |
| `File.NotScanned` | 409 | — | The virus scan hasn't passed (yet) |
| `Document.SharedWithDoctor` | 409 | — | Deleting a document attached to a visit |
| `Consent.Required` | 400 | — | Booking without accepting the terms |
| `Booking.ServiceNotOffered` | 400 | — | The doctor doesn't offer the service at that branch |
| `Slot.CapacityReached` | 409 | — | The slot is full or no longer free (replaces `Appointment.SlotAlreadyBooked` and `Appointment.SlotNotAvailable` on `/api/v2`) |
| `Deposit.NotOffered` | 409 | — | The service has no optional deposit |
| `Deposit.AlreadyPaid` | 409 | — | The deposit is paid |
| `Payment.GatewayUnavailable` | 503 | — | Paymob can't be reached |
| `Invoice.NotFound` | 404 | — | Unknown invoice, or not the caller's |
| `Invoice.NothingDue` | 409 | — | The invoice is paid, void or has no balance |
| `Visit.NotFound` | 404 | — | Unknown or unpublished visit, or not the caller's |
| `Visit.NoPrescription` | 404 | — | The visit has no prescription |
| `Reminder.LinkInvalid` | 404 | — | Unknown or expired reminder token |
| `Payer.NotFound` | 404 | — | Unknown or inactive payer |
| `Idempotency.KeyReused` | 409 | — | The same `Idempotency-Key` with a different body |
| `RateLimit.Exceeded` | 429 | `retryAt` | As in §2.7; `Retry-After` is still sent |

### 13.11 Frontend changes this section implies

Compared with the mock backend at `16a6dcb`. Screens keep working; most items are for the HTTP adapters.

| # | Change | Where |
|---|---|---|
| 1 | `NotificationChannel` is `Phone`, not `Sms` (decided 2026-10-26: WhatsApp first, SMS as the fallback) | Already in progress on `feature/p2-ui-front-desk` |
| 2 | The reset link carries `userId`, not `email`: `resetWithEmailToken(userId, token, newPassword)` | `PasswordResetService`, `use-account-access.ts`, the mock's link |
| 3 | Login, MFA, sign-up verification, invitation acceptance and refresh return a `Session` with `user: Me`; the V1 `LoginResponse` fields (`email`, `fullName`, `role`) are gone. `saveSession` keeps the tokens and `expiresAt`, and the current user comes from `user` | `shared/api/http-client.ts`, the auth adapters |
| 4 | `SignInResult.status` is `SignedIn` / `MfaRequired` (PascalCase like every enum); the adapter maps it to the interface's `signed-in` / `mfa-required` | Auth adapter |
| 5 | The patient's appointments, visits, invoices and documents are *paged*. The adapters read `X-Total-Count` and fetch further pages (`pageSize=100`) until they have every item, or the screens move to paging | Those four adapters |
| 6 | `PATCH /me` and `POST /me/phone/confirm` return `Me`, not `PatientProfile`; the adapter re-reads `GET /me/patient-profile` (or the hook invalidates both queries) | `ProfileService` adapter |
| 7 | An invalid phone is a 400 validation error on the field (`errors.phone`), not the code `Validation.InvalidPhone`; the adapter maps it to that code, or the form shows the field error | Phone forms |
| 8 | A document can be downloaded only after its scan: `File.NotScanned` (409) right after an upload needs a message ("still being checked") | Documents screen, `common:errors` |
| 9 | Not signed in is a 401 without a code (shape E), not `Auth.Unauthorized`; the HTTP client already handles it | — |
| 10 | `AppNotification.link.kind` is PascalCase (`Appointment`, `Invoice`, `Visit`); the adapter maps it to the interface's lower-case values. (`category` is only a query parameter, so its lower-case values work as they are) | Notifications adapter |
| 11 | Translations for the codes the mocks never throw: `Auth.MfaEnrollmentRequired`, `Auth.EmailAlreadyExists`, `Messaging.PhoneUnreachable`, `Branch.NotFound`, `DoctorApplication.NotFound`, `Booking.ServiceNotOffered`, `Deposit.NotOffered`, `Deposit.AlreadyPaid`, `Document.SharedWithDoctor`, `Payment.GatewayUnavailable`, `Idempotency.KeyReused`, `RateLimit.Exceeded`, `File.TypeNotAllowed`, `File.TooLarge`, `File.NotScanned` | `common:errors` (en, ar) |
| 12 | Suggested: a test per mock service that checks its error codes against §13.3–§13.8, so the mocks can't drift from this section before the HTTP adapters exist | `src/mocks` |

### 13.12 Revisions of this section

| Date | Change |
|---|---|
| 2026-10-27 | First version: auth and sessions, me and profile, public reference data and the doctor directory, doctor applications, the patient's appointments, queue and reminders, visits, invoices, documents, insurance and notifications. Settles P1 plan Q-A |
| 2026-10-27 | Front desk and nurse station added (§13.13–§13.21), from the frontend's interfaces at `22b7f00`: patient search, duplicates and registration; the desk's day, check-in, no-show, booking on behalf and walk-ins; the queue board, waiting-room screen and `WatchBranch` on `/hubs/queue`; triage keyed by appointment (built in P2); checkout lines, desk payments, receipts and cash sessions. Replaces five 07 routes (§13.13). `Idempotency-Key` also on booking on behalf, walk-ins and desk payments |

### 13.13 Front desk and nurse station: conventions

Added on 2026-10-27 from the frontend's interfaces at `22b7f00` (`src/features/{patients,desk,queue,triage,billing}/api/*.service.ts`). §13.1 applies; in addition:

- **Branch scope.** Every route here checks its permission against the record's branch (ADR 0002): a branch in the route, the appointment's branch, or the drawer's branch. Outside the user's scope: 403 `Access.OutOfScope`. A permission missing entirely is 403 without a code (shape E).
- **"Today"** is the branch's date in its own time zone ([ADR 0003](v2/adr/0003-branch-time-zones.md)).
- **The settings the frontend now keeps as constants** (check-in opens 60 min before the visit, no-show after 15 min, 5 unslotted walk-ins per doctor a day, a cash variance above EGP 50 needs approval) are organization settings on the server (FR-ORG-05, P1 PR 3), read with `GET /api/v2/organization/settings`. The routes below enforce them; the frontend can keep its constants until it reads that endpoint.
- **Triage is keyed by appointment**, not by encounter as [07 §4](v2/07_API_DESIGN.md#patients-and-clinical) had it, so the desk never needs an encounter id. It is built in **P2** with the queue, so a visit can move past `WaitingTriage` before the EMR exists; P4 attaches the recorded vitals to the visit's encounter without changing the routes.
- 07 routes replaced by these: `PUT /branches/{id}/queue/order` (by `move-to-front` with a reason), `GET /appointments/{id}/amount-due` (by `check-in` and `checkout`), `POST /payments` from the desk (by `POST /appointments/{id}/payments`), invoice lines before issue (by the checkout lines), and `POST /encounters/{id}/vitals` (by triage).

### 13.14 Front desk and nurse station: types

```ts
// ---- patients at the desk ----
export type Gender = "Male" | "Female";
export interface PatientSummary {
  id: Id; mrn: string; /* "MRN-000412", sequential per organization */ fullName: string; phone: Phone;
  gender?: Gender; dateOfBirth?: DateOnly; nationalId?: string;
  hasPortalAccount: boolean;    // a desk-registered patient may have no login
  registeredAt: Instant;
  lastVisit?: { date: DateOnly; doctorName: string };
  balanceDue: number;           // across issued invoices
  insurance?: { payerName: string; status: PolicyStatus };   // the default card
}
export interface RegisterPatientRequest {
  fullName: string; phone: string; gender: Gender; dateOfBirth: DateOnly; nationalId?: string;
  emergencyContactName?: string; emergencyContactPhone?: string;
}

// ---- the desk's day ----
export interface Room { id: Id; nameEn: string; nameAr: string }
export interface DeskAppointment {
  id: Id; reference: string; status: AppointmentStatus; source: BookingSource;
  patient: { id: Id; fullName: string; mrn: string; phone: Phone; dateOfBirth?: DateOnly };
  doctorId: Id; doctorName: string; specialtyId: Id; serviceId: Id; branchId: Id;
  date: DateOnly; startTime: TimeOfDay; endTime: TimeOfDay; startsAtUtc: Instant;
  price: number; depositAmount?: number; depositPaid: boolean; attendanceConfirmed: boolean;
  checkedInAt?: Instant; completedAt?: Instant;
  queue?: { ticketNo: string; stage: QueueStage };
  room?: Room;
  amountDue: number;            // services − paid deposit − payments
  invoiced: boolean;            // the first payment issues the invoice
}
export interface CheckInView {
  appointment: DeskAppointment;
  patient: { phone: Phone; phoneVerified: boolean; nationalId?: string; gender?: Gender; dateOfBirth?: DateOnly };
  charges: { price: number; depositPaid: number; due: number };
  insurance?: {
    payerName: string; planName?: string; memberNo: string; coveragePercent?: number;
    validTo: DateOnly; status: PolicyStatus;
  };
}
export interface DeskBookRequest {
  patientId: Id; doctorId: Id; branchId: Id; serviceId: Id; slotId: Id; source: "Phone" | "Desk";
}
export interface WalkInRequest { patientId: Id; doctorId: Id; branchId: Id; serviceId: Id; complaint?: string }

// ---- the queue ----
export interface QueueEntry {
  appointmentId: Id; ticketNo: string; stage: QueueStage;
  position: number;             // branch-wide while WaitingTriage, per doctor once ReadyForDoctor
  patientId: Id; patientName: string; gender?: Gender; dateOfBirth?: DateOnly; mrn: string;
  doctorId: Id; doctorName: string; specialtyId: Id; serviceId: Id; room?: Room; source: BookingSource;
  startTime?: TimeOfDay;        // none for an unslotted walk-in
  checkedInAt: Instant; triagedAt?: Instant; calledAt?: Instant; completedAt?: Instant;
  lateMinutes: number;          // checked in this long after the booked start; 0 when on time
  priorityReason?: string;      // set by move-to-front
  vitals?: { systolic?: number; diastolic?: number; pulse?: number };
  allergies: { name: string; severity?: "Mild" | "Moderate" | "Severe" }[];
  complaint?: string;
  amountDue: number;
}
export interface DisplayRoom {
  doctorId: Id; doctorName: string; specialtyId: Id; room?: Room;
  nowServing?: string; next: string[]; /* the next two tickets */ waitingCount: number; averageWaitMinutes: number;
}
export interface WaitingDisplay { branchId: Id; rooms: DisplayRoom[]; updatedAt: Instant }   // tickets only, no names

// ---- triage ----
export interface Vitals {
  systolic?: number; diastolic?: number; pulse?: number; temperature?: number; /* °C */
  respiratoryRate?: number; spo2?: number; /* % */ weightKg?: number; heightCm?: number; painScore?: number; /* 0–10 */
}
export interface TriageForm extends Vitals { chiefComplaint?: string; allergiesConfirmed: boolean }
export interface TriageCase {
  entry: QueueEntry;
  patient: {
    mrn: string; fullName: string; gender?: Gender; dateOfBirth?: DateOnly; bloodType?: string;
    allergies: { name: string; severity?: "Mild" | "Moderate" | "Severe" }[]; conditions: string[];
  };
  draft?: { form: TriageForm; savedAt: Instant };
}

// ---- checkout and cash ----
export type DeskPaymentMethod = "Cash" | "Card" | "Wallet";
export interface ChargeLine { id: Id; serviceId: Id; quantity: number; unitPrice: number; total: number; booked: boolean }
export interface Checkout {
  appointment: DeskAppointment; lines: ChargeLine[];
  subtotal: number; depositPaid: number; paidSoFar: number; due: number;
  invoice?: { id: Id; number: string; status: "Issued" | "PartiallyPaid" | "Paid" };   // lines are fixed once it exists
  addableServiceIds: Id[];      // services the doctor offers
  insurance?: { payerName: string; coveragePercent?: number };   // shares arrive in P5
}
export interface CollectRequest {
  method: DeskPaymentMethod; amount: number; /* > 0, at most what's due */
  tendered?: number; /* cash handed over */ reference?: string; /* POS or wallet reference */
}
export interface Receipt {
  paymentId: Id; receiptNo: string; invoiceNumber: string; /* "<BRANCH>-<YYYY>-<000001>" */
  method: DeskPaymentMethod; amount: number; change: number; paidAt: Instant; remaining: number;
}
export interface CashDrawer { id: Id; branchId: Id; nameEn: string; nameAr: string; inUseBy?: string }
export type CashSessionStatus = "Open" | "Closed" | "Approved";
export interface CashTransaction {
  id: Id; receiptNo: string; at: Instant; kind: "Payment" | "Refund"; patientName: string;
  serviceId?: Id; method: DeskPaymentMethod; amount: number;   // refunds are negative
}
export interface CashSession {
  id: Id; branchId: Id; drawer: CashDrawer; openedAt: Instant; openedByName: string; openingFloat: number;
  status: CashSessionStatus; cashIn: number; cashOut: number;
  expectedCash: number;         // float + cash in − cash out
  byMethod: { method: DeskPaymentMethod; amount: number; count: number }[];
  transactions: CashTransaction[];
  closedAt?: Instant; countedAmount?: number; variance?: number; needsApproval?: boolean; note?: string;
}
```

### 13.15 Patients at the desk

| Method and path | Permission | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/patients` | `patients.view` | `q` (required, at least 2 characters: name in either language, phone or part of it, MRN, national ID, or a `yyyy-MM-dd` date of birth; FR-FD-01), `filter?` (`All`, `Balance`, `Insured`) | 200 `PatientSummary[]`: **at most 20 best matches, not paged** (a search, not a list) | 400 validation | P2 |
| `GET /api/v2/patients/duplicates` | `patients.register` | `phone?`, `fullName?`, `dateOfBirth?` | 200 `PatientSummary[]`, *full* (at most 10): same phone, or same name and date of birth (FR-FD-02) | 400 validation | P2 |
| `GET /api/v2/patients/{patientId}` | `patients.view` | — | 200 `PatientSummary` | 404 `Patient.NotFound` | P2 |
| `POST /api/v2/patients` | `patients.register` | `RegisterPatientRequest` | 201 `PatientSummary` with the next MRN. **A possible duplicate never refuses**: the form checks `duplicates` first | 400 validation (`phone`, `dateOfBirth` not in the future) | P2 |
| `GET /api/v2/patients/{patientId}/upcoming-appointments` | `appointments.view` | — | 200 `DeskAppointment[]`, *full*, from today on, in the branches the user's scope covers | 404 `Patient.NotFound` | P2 |

### 13.16 The desk's day, check-in, booking and walk-ins

| Method and path | Permission | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/branches/{branchId}/calendar` | `appointments.view` | `date` (required), `doctorId?` | 200 `DeskAppointment[]`, *full*: every visit at the branch that day, whatever its status, in time order | 400 validation; 403 `Access.OutOfScope`; 404 `Branch.NotFound` | P2 |
| `GET /api/v2/appointments/{appointmentId}/check-in` | `appointments.check_in` | — | 200 `CheckInView` | 403 `Access.OutOfScope`; 404 `Appointment.NotFound` | P2 |
| `POST /api/v2/appointments/{appointmentId}/check-in` | `appointments.check_in` | — | 200 `DeskAppointment` (`CheckedIn`, with a queue ticket; FR-FD-03) | 400 `Appointment.CheckInWindowClosed`; 403 `Access.OutOfScope`; 404 `Appointment.NotFound`; 409 `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/appointments/{appointmentId}/no-show` | `appointments.mark_no_show` | — | 200 `DeskAppointment` (`NoShow`; FR-APT-07) | 400 `Appointment.NoShowTooEarly`; 403, 404 as above; 409 `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/appointments/book-on-behalf` | `appointments.book_on_behalf`; `Idempotency-Key` | `DeskBookRequest` | 201 `DeskAppointment`, always `Confirmed` (FR-APT-03) | 400 validation, `Appointment.SlotInPast`, `Booking.ServiceNotOffered`; 403 `Access.OutOfScope`; 404 `Patient.NotFound`, `Appointment.SlotNotFound`; 409 `Slot.CapacityReached`, `Idempotency.KeyReused` | P2 |
| `POST /api/v2/appointments/walk-in` | `appointments.check_in`; `Idempotency-Key` | `WalkInRequest` | 201 `DeskAppointment`, already `CheckedIn`: the doctor's free slot if one starts within 30 min, otherwise an unslotted queue place (FR-APT-04) | 400 validation, `Booking.ServiceNotOffered`; 403 `Access.OutOfScope`; 404 `Patient.NotFound`; 409 `Appointment.DoctorNotAvailable` (no shift at the branch today), `Appointment.WalkInCapReached`, `Idempotency.KeyReused` | P2 |

### 13.17 The queue board, the waiting-room screen and live updates

| Method and path | Permission | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/branches/{branchId}/queue` | `queue.view` | `doctorId?` | 200 `QueueEntry[]`, *full*: today's checked-in visits in every stage | 403 `Access.OutOfScope`; 404 `Branch.NotFound` | P2 |
| `GET /api/v2/branches/{branchId}/queue/display` | `queue.view`, or the branch's display device token | — | 200 `WaitingDisplay`: tickets and rooms, no names (FR-FD-06) | 403 `Access.OutOfScope`; 404 `Branch.NotFound` | P2 |
| `POST /api/v2/appointments/{appointmentId}/call` | `queue.manage`, or the visit's doctor | — | 200 `QueueEntry` (`WithDoctor`; the appointment becomes `InProgress`; FR-FD-05) | 403 `Access.OutOfScope`; 404 `Appointment.NotFound`; 409 `Appointment.DoctorBusy` (the doctor already has someone in the room), `Appointment.InvalidStatusTransition` | P2 |
| `POST /api/v2/appointments/{appointmentId}/queue/move-to-front` | `queue.manage` | `{ reason }` (required, ≤ 200; audited) | 200 `QueueEntry` (first in its line, `priorityReason` set) | 400 validation (`reason`); 403, 404 as above; 409 `Appointment.InvalidStatusTransition` (not waiting) | P2 |

**Live updates.** The hub `/hubs/queue` of §13.7. A staff client calls `WatchBranch(branchId)` (needs `queue.view` in that branch) and receives `QueueChanged(branchId)`, with no payload, whenever anything on that branch's queue changes; it then refetches what it shows. `UnwatchBranch(branchId)` stops it. The waiting-room screen connects with the display device token instead. How a device token is issued (FR-FD-06) comes with the branch settings screens; until then the display works under a staff login.

### 13.18 Triage

| Method and path | Permission | Body | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/appointments/{appointmentId}/triage` | `vitals.write` | — | 200 `TriageCase` | 403 `Access.OutOfScope`; 404 `Appointment.NotFound` | P2 |
| `PUT /api/v2/appointments/{appointmentId}/triage/draft` | `vitals.write` | `TriageForm` | 200 `{ savedAt }`. Keeps unfinished vitals (one draft per visit, replaced each time) | 400 `Vitals.OutOfRange`; 403, 404 as above; 409 `Appointment.InvalidStatusTransition` (not `WaitingTriage`) | P2 |
| `POST /api/v2/appointments/{appointmentId}/triage` | `vitals.write` | `TriageForm` | 200 `QueueEntry` (`ReadyForDoctor`). Records the vitals with who took them and when, and deletes the draft (FR-EMR-02) | 400 `Vitals.OutOfRange` (a value no person can have; outside the normal range is only flagged), `Triage.AllergiesNotConfirmed`; 403, 404 as above; 409 `Appointment.InvalidStatusTransition` | P2 |

Accepted ranges, as in the frontend: systolic 50–260, diastolic 30–160, pulse 20–250, temperature 30–45 °C, respiratory rate 4–60, SpO₂ 50–100 %, weight 0.5–350 kg, height 30–250 cm, pain 0–10. Vitals are clinical data: they are audited with their values redacted (FR-AUD-01).

### 13.19 Checkout and cash sessions

| Method and path | Permission | Body or query | Success | Errors | Phase |
|---|---|---|---|---|---|
| `GET /api/v2/branches/{branchId}/checkout` | `payments.collect` | — | 200 `DeskAppointment[]`, *full*: today's checked-in visits with something left to pay | 403 `Access.OutOfScope`; 404 `Branch.NotFound` | P3 |
| `GET /api/v2/appointments/{appointmentId}/checkout` | `payments.collect` | — | 200 `Checkout` | 403 `Access.OutOfScope`; 404 `Appointment.NotFound` | P3 |
| `POST /api/v2/appointments/{appointmentId}/checkout/lines` | `payments.collect` | `{ serviceId }` | 200 `Checkout` (FR-BIL-02) | 400 `Booking.ServiceNotOffered` (not in `addableServiceIds`); 403, 404 as above; 404 `Service.NotFound`; 409 `Invoice.NotDraft` | P3 |
| `DELETE /api/v2/appointments/{appointmentId}/checkout/lines/{lineId}` | `payments.collect` | — | 200 `Checkout` | 403, 404 as above; 404 `Invoice.LineNotFound`; 409 `Invoice.NotDraft`, `Invoice.BookedLineNotRemovable` | P3 |
| `POST /api/v2/appointments/{appointmentId}/payments` | `payments.collect`; `Idempotency-Key` | `CollectRequest` | 201 `Receipt`. The first payment issues the invoice; the payment goes into the user's open cash session, which cash requires | 400 validation, `Payment.InvalidAmount`; 403, 404 as above; 409 `Invoice.NothingDue`, `Payment.NoOpenCashSession`, `Idempotency.KeyReused` | P3 |
| `GET /api/v2/payments/{paymentId}/receipt` | `payments.collect` | — | 200 PDF (attachment, FR-BIL-07). `Accept-Language` picks its language | 403 `Access.OutOfScope`; 404 `Payment.NotFound` | P3 |
| `GET /api/v2/me/cash-session` | `cash_sessions.operate` | — | 200 `CashSession` (the user's open one), or 204 when none | — | P3 |
| `GET /api/v2/branches/{branchId}/cash-drawers` | `cash_sessions.operate` | — | 200 `CashDrawer[]`, *full*, with who has each open | 403 `Access.OutOfScope`; 404 `Branch.NotFound` | P3 |
| `POST /api/v2/cash-sessions` | `cash_sessions.operate` | `{ drawerId, openingFloat }` (≥ 0) | 201 `CashSession` (`Open`; one per user, FR-BIL-09) | 400 validation; 403 `Access.OutOfScope`; 404 `CashDrawer.NotFound`; 409 `CashSession.AlreadyOpen`, `CashDrawer.InUse` | P3 |
| `POST /api/v2/cash-sessions/{sessionId}/close` | `cash_sessions.operate` (own session) | `{ countedAmount, note? }` | 200 `CashSession` (`Closed`, with `variance` and `needsApproval`); it can't change afterwards | 400 validation; 404 `CashSession.NotFound` (also someone else's); 409 `CashSession.NotOpen` | P3 |
| `GET /api/v2/cash-sessions/{sessionId}/report` | `cash_sessions.operate` (own), or `cash_sessions.reconcile` | — | 200 PDF: the interim (X) report while open, the closing (Z) report once closed | 404 `CashSession.NotFound` | P3 |

### 13.20 Front desk and nurse station: error codes

In addition to [§13.10](#1310-error-codes).

| Code | Status | When |
|---|---|---|
| `Access.OutOfScope` | 403 | The record's branch is outside the user's scope for the permission |
| `Patient.NotFound` | 404 | Unknown patient record |
| `Appointment.CheckInWindowClosed` | 400 | Checking in more than 60 min (the setting) before the visit, or after its day |
| `Appointment.NoShowTooEarly` | 400 | Less than 15 min (the setting) after the start |
| `Appointment.DoctorNotAvailable` | 409 | A walk-in for a doctor with no shift at the branch today |
| `Appointment.WalkInCapReached` | 409 | The doctor's unslotted walk-ins for today are used up (5, the setting) |
| `Appointment.DoctorBusy` | 409 | Calling a patient in while the doctor has someone in the room |
| `Vitals.OutOfRange` | 400 | A value outside the accepted ranges of §13.18 |
| `Triage.AllergiesNotConfirmed` | 400 | Submitting triage with `allergiesConfirmed: false` |
| `Service.NotFound` | 404 | Unknown or inactive service |
| `Invoice.NotDraft` | 409 | Changing lines after the invoice is issued |
| `Invoice.LineNotFound` | 404 | Unknown line |
| `Invoice.BookedLineNotRemovable` | 409 | Removing the booked service's line |
| `Payment.InvalidAmount` | 400 | Zero, negative, or more than what's due |
| `Payment.NoOpenCashSession` | 409 | A cash payment without an open cash session |
| `Payment.NotFound` | 404 | Unknown payment |
| `CashSession.AlreadyOpen` | 409 | The user already has an open session |
| `CashSession.NotOpen` | 409 | Closing a session that is closed |
| `CashSession.NotFound` | 404 | Unknown session, or another user's |
| `CashDrawer.InUse` | 409 | Someone else has a session open on the drawer |
| `CashDrawer.NotFound` | 404 | Unknown drawer, or one at another branch |

### 13.21 Front desk and nurse station: frontend changes

Compared with the mock backend at `22b7f00`.

| # | Change | Where |
|---|---|---|
| 1 | The walk-in also sends an `Idempotency-Key`, like booking on behalf (a double tap must not create two walk-ins) | `DeskService.walkIn` adapter |
| 2 | A missing move-to-front reason is a 400 validation error on `reason`, not the code `Queue.ReasonRequired`; the adapter maps it, or the form shows the field error | Queue board |
| 3 | Messages for the codes the mocks don't throw yet: `Appointment.DoctorNotAvailable`, `Invoice.LineNotFound`, `Invoice.BookedLineNotRemovable`, `CashSession.NotFound` (`CashDrawer.NotFound` and `Payment.NotFound` too, if missing) | `common:errors` (en, ar), `mocks/contract-errors.test.ts` |
| 4 | The waiting-room screen will sign in with a display device token instead of a staff login, once the branch settings screens issue them; nothing to do yet | — |
| 5 | Later: read the check-in window, no-show grace, walk-in cap and variance threshold from `GET /api/v2/organization/settings` instead of `desk/model/policy.ts` and `cash-session.ts` | When the settings endpoint exists (P1) |
