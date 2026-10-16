# ClinicFlow API Contract

| | |
|---|---|
| **Contract version** | 1.0.0 |
| **Last updated** | 2026-10-09 |
| **Applies to** | ClinicFlow backend (`ClinicFlow.Backend`) and the ClinicFlow frontend (React) |
| **Canonical copy** | `ClinicFlow.Backend/docs/API_CONTRACT.md`. The frontend repo keeps an identical copy at `docs/API_CONTRACT.md` |
| **Machine-readable companion** | [`openapi.json`](openapi.json) (OpenAPI 3.1, generated from the running API) |

This document is the single agreement between the two apps. It describes what the API **does today**, including known quirks, so the frontend can rely on it. Planned changes are listed separately in [§9](#9-pending-contract-changes) and become real only when they move into the changelog ([§10](#10-changelog)). The rules for changing the contract are in [§11](#11-how-to-change-this-contract).

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
| Slot and schedule times | **Clinic local wall-clock time**, with no time zone. Show them as they are; do not convert |
| Timestamps (`DateTime`) | UTC. ⚠️ Values read from the database are serialized **without** a `Z` suffix (e.g. `"bookedAt": "2026-10-08T12:17:34.1027961"`). Treat every timestamp as UTC: append `Z` before parsing if it is missing. `LoginResponse.expiresAt` does include the `Z` |
| Money | `consultationFee` is a JSON number (decimal, EGP), e.g. `500.00` |
| Ids | Entity ids are integers. User ids (`UserProfileResponse.id`, `confirm-email` / `reset-password` `userId`) are GUID strings |
| Nulls | Optional fields may be `null` or omitted in requests. Nullable response fields are always present, possibly as `null` |
| Lists | List endpoints return a **bare JSON array**, with no total count and no envelope |
| Paging | `pageNumber` (1-based) and `pageSize` query parameters, where supported. See each endpoint for defaults and limits |
| Partial updates | `PATCH` endpoints, and `PUT /api/doctors/schedule/{id}`, apply only the fields you send. Omitted or `null` fields keep their current value |
| Route ids | A non-integer `{id}` returns 400 |
| CORS | Allowed origin: the configured frontend URL (default `http://localhost:3000`). Any header or method; credentials allowed |

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
- **Unapproved doctors can log in** (*REVIEW M1*). They just don't appear in search.

### 2.3 Refresh rules (frontend MUST follow these)
1. **Refresh once at a time (single-flight).** When several requests get 401 at the same moment, make one refresh call and let the other requests wait for its result. Two parallel refreshes with the same token: one succeeds, the other gets 401.
2. **Never reuse an old refresh token.** Replace the stored token as soon as a refresh succeeds. Reusing an already-rotated token from a **different IP, or more than 5 minutes later**, is treated as theft and **revokes every session of that user** (all devices). Reuse from the same IP within 5 minutes is only rejected with 401.
3. **On 401 `Auth.InvalidRefreshToken`, the session is over:** clear the tokens and send the user to login. This happens when the token is expired, revoked, rotated, the account was suspended, or the password was reset.
4. **A 5xx from refresh is not a logout:** keep the tokens and retry later.

### 2.4 Detecting an expired access token
A protected endpoint called with a missing, invalid or expired access token returns **401 with an empty body** and a `WWW-Authenticate: Bearer …` header (it contains `error="invalid_token"` when the token is expired or invalid). Refresh and retry once; if the retry also returns 401, log out.

### 2.5 Authorization failures
- A **wrong role** (e.g. a patient calling `/api/admin/*`) returns **403 with an empty body**.
- A **right role but not the owner** of the resource (e.g. another patient's appointment) returns **403 with a ProblemDetails body** whose `title` is the error code (e.g. `Appointment.Unauthorized`).

### 2.6 Logout
`POST /api/auth/logout` requires a valid access token **and** the refresh token in the body. It revokes only the caller's own refresh token. If the access token has already expired, refresh first. Whatever the response, clear the local tokens.

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
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email must be a valid email address."],
    "Password": ["Password must be at least 8 characters long.", "Password must contain at least one digit."]
  }
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

**D. Malformed request (JSON or binding error).** Status 400. Keys are JSON paths such as `"$.email"`, or `"request"`. This usually indicates a frontend bug.

**E. Empty body.** Used for 401 from the auth middleware, 403 for a wrong role, 404 for an unknown route, and unhandled 500s.

### 3.2 Status codes

| Status | Meaning in this API | Body |
|---|---|---|
| 200 | OK, with a body | Resource or array |
| 201 | Created, with a body (no `Location` header) | Created resource |
| 204 | Done, no body | — |
| 400 | Invalid input (B, C or D) | Validation shape |
| 401 | Not authenticated, bad credentials, or a session problem | A (auth endpoints) or E (middleware) |
| 403 | Wrong role (E) or not the owner (A) | E or A |
| 404 | Resource not found (A), or unknown route (E) | A or E |
| 409 | Conflict with the current state (A) | A |
| 500 | Unexpected server error. ⚠️ Some *expected* errors also return 500 today (*REVIEW M3*); they are marked in the catalog below | A or E |

### 3.3 Error code catalog

Only codes the API can actually return are listed.

| Code | Status | When |
|---|---|---|
| `Auth.InvalidCredentials` | 401 | Wrong email or password |
| `Auth.AccountLockedOut` | 401 | Too many failed logins; locked for 15 min |
| `Auth.AccountNotActive` | 401 | Account suspended |
| `Auth.EmailNotConfirmed` | 401 | Email not confirmed yet |
| `Auth.InvalidRefreshToken` | 401 | Refresh token invalid, expired, revoked or rotated, or the user is suspended; on logout, also when the token is not the caller's |
| `Auth.UserNotFound` | 404 | `userId` unknown (confirm email, reset password) |
| `Auth.EmailAlreadyExists` | **500** *(M3)* | Registration with an email that is already used |
| `Auth.RegistrationFailed` | **500** *(M3)* | Identity rejected the user, or registration failed; `detail` lists the reasons |
| `Auth.InvalidEmailConfirmationToken` | **500** *(M3)* | Confirmation token invalid or expired |
| `Auth.InvalidPasswordResetToken` | **500** *(M3)* | Reset token invalid or expired, **or** the new password fails the password policy |
| `Doctor.NotFound` | 404 | Doctor profile id unknown |
| `Doctor.ProfileNotFound` | 404 | The logged-in user has no doctor profile |
| `Doctor.ScheduleNotFound` | 404 | Schedule unknown or owned by another doctor |
| `Doctor.ScheduleDayAlreadyExists` | **500** *(M3)* | A schedule for that weekday already exists |
| `Doctor.InvalidScheduleTime` | 400 (shape C) | Schedule update would leave end ≤ start, or not fit one slot |
| `Patient.ProfileNotFound` | 404 | The logged-in user has no patient profile |
| `Appointment.NotFound` | 404 | Appointment id unknown |
| `Appointment.SlotNotFound` | 404 | Slot id unknown (booking) |
| `Appointment.SlotNotAvailable` | 409 | Slot is booked or blocked |
| `Appointment.SlotAlreadyBooked` | 409 | Someone else booked the slot first |
| `Appointment.SlotInPast` | 400 (shape C) | The slot has already started |
| `Appointment.CancellationWindowExpired` | 400 (shape C) | Patient cancelling less than 2 hours before the start |
| `Appointment.AlreadyCancelled` | 409 | Already cancelled. Also returned when an **admin** cancels a *completed* appointment *(M14)* |
| `Appointment.AlreadyCompleted` | 409 | Already completed |
| `Appointment.CannotCompleteCancelled` | **500** *(M3)* | Completing a cancelled appointment |
| `Appointment.DoctorNotesNotAllowed` | **500** *(M3)* | Notes on a cancelled appointment |
| `Appointment.Unauthorized` | 403 | Not the owner of the appointment |
| `Appointment.ReviewAlreadyExists` | 409 | Appointment already reviewed |
| `Appointment.ReviewNotAllowed` | 400 (shape C) | Appointment not completed |
| `Review.NotFound` | 404 | Review id unknown |
| `Review.Unauthorized` | 403 | Not the review's author (and not an admin) |
| `Notification.NotFound` | 404 | Notification id unknown |
| `Notification.Unauthorized` | 403 | Notification belongs to another user |
| `Profile.NotFound` | 404 | User not found |
| `Profile.UpdateFailed` | **500** *(M3)* | Identity rejected the profile update |
| `Specialty.NotFound` | 404 | Specialty unknown or inactive |
| `Specialty.NameAlreadyExists` | **500** *(M3)* | Duplicate specialty name |
| `Specialty.HasAssignedDoctors` | **500** *(M3)* | Deleting a specialty that doctors use |
| `Admin.DoctorAlreadyApproved` | **500** *(M3)* | Approving an approved doctor |
| `Admin.DoctorAlreadySuspended` | **500** *(M3)* | Suspending a suspended doctor |

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
| 5 | POST | `/api/auth/logout` | Any | 204 |
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
| 19 | GET | `/api/appointments/my` | Patient | 200 `AppointmentResponse[]` |
| 20 | GET | `/api/appointments/doctor/my` | Doctor | 200 `AppointmentResponse[]` |
| 21 | GET | `/api/appointments/{id}` | Any (owner or admin) | 200 `AppointmentResponse` |
| 22 | POST | `/api/appointments/{id}/cancel` | Patient, Doctor, Admin | 204 |
| 23 | POST | `/api/appointments/{id}/complete` | Doctor | 204 |
| 24 | PUT | `/api/appointments/{id}/notes` | Doctor | 204 |
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
| 32 | GET | `/api/notifications/my?unreadOnly=` | Any | 200 `NotificationResponse[]` |
| 33 | POST | `/api/notifications/{id}/read` | Any | 204 |
| 34 | POST | `/api/notifications/read-all` | Any | 204 |
| | **Specialties** | | | |
| 35 | GET | `/api/Specialty?includeInactive=` | — (Admin for `includeInactive=true`) | 200 `SpecialtyResponse[]` |
| 36 | GET | `/api/Specialty/{id}` | — | 200 `SpecialtyResponse` |
| 37 | POST / PUT / DELETE | `/api/Specialty[/{id}]` | Admin | *Deprecated: use #40–#43* |
| | **Admin** | | | |
| 38 | GET | `/api/admin/doctors/pending` | Admin | 200 `DoctorResponse[]` |
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

Each entry lists the request, the success response, and the errors that endpoint can return (codes from [§3.3](#33-error-code-catalog)). Every authenticated endpoint can also return 401 (empty body) and, for a wrong role, 403 (empty body); these are not repeated below.

### 5.1 Auth

**1. `POST /api/auth/register/patient`** (anonymous)
- **Body:** `RegisterPatientRequest`. Rules:

  | Field | Rule |
  |---|---|
  | `firstName`, `lastName` | Required, ≤ 50 chars |
  | `email` | Required, valid email |
  | `password` | Required, ≥ 8 chars, with upper case, lower case, a digit and a special character |
  | `phoneNumber` | Required, E.164 (`^\+?[1-9]\d{1,14}$`) |
  | `gender` | Always send it. It is not validated: if omitted, the server stores `Male` |
  | `dateOfBirth` | Optional; the user must be at least 18 |
  | `emergencyContactPhone` | Optional, E.164 |
- **200:** `RegisterResponse`. A confirmation email is sent; the link opens the frontend route `/#/confirm-email` ([§8](#8-frontend-routes-the-backend-depends-on)). If the email fails to send, registration still succeeds.
- **Errors:**
  - 400 validation (B)
  - 500 `Auth.EmailAlreadyExists`, `Auth.RegistrationFailed`

**2. `POST /api/auth/register/doctor`** (anonymous)
- **Body:** `RegisterDoctorRequest`. Same rules for name, email, password, phone and date of birth. `specialtyId` must be an existing specialty. `yearsOfExperience`, `consultationFee`, `bio` (≤ 500), `clinicAddress` and `clinicCity` are not validated yet *(M13)*.
- **200:** `RegisterResponse`. The doctor must confirm their email; an admin must approve them before they appear in search.
- **Errors:**
  - 400 (B)
  - 500 `Auth.EmailAlreadyExists`, `Auth.RegistrationFailed` (also for an invalid `specialtyId`)

**3. `POST /api/auth/login`** (anonymous)
- **Body:** `{ "email": string, "password": string }`
- **200:** `LoginResponse`
- **Errors:** 401 `Auth.InvalidCredentials`, `Auth.AccountLockedOut`, `Auth.AccountNotActive`, `Auth.EmailNotConfirmed`

**4. `POST /api/auth/refresh-token`** (anonymous)
- **Body:** `{ "refreshToken": string }`
- **200:** `LoginResponse` with a **new** access and refresh token. Follow the rules in [§2.3](#23-refresh-rules-frontend-must-follow-these).
- **Errors:** 401 `Auth.InvalidRefreshToken`

**5. `POST /api/auth/logout`** (any logged-in user)
- **Body:** `{ "refreshToken": string }`
- **204**
- **Errors:** 401 `Auth.InvalidRefreshToken` (the token is unknown or isn't the caller's)

**6. `POST /api/auth/confirm-email`** (anonymous)
- **Body:** `{ "userId": "<guid>", "token": string }`, both taken from the link query string as they are ([§8](#8-frontend-routes-the-backend-depends-on)).
- **204**
- **Errors:**
  - 404 `Auth.UserNotFound`
  - 500 `Auth.InvalidEmailConfirmationToken`

**7. `POST /api/auth/forgot-password`** (anonymous)
- **Body:** `{ "email": string }`
- **204** whether or not the email exists, so callers can't discover registered emails. The email links to `/#/reset-password`.
- **Errors:** 500 if the email cannot be sent.

**8. `POST /api/auth/reset-password`** (anonymous)
- **Body:** `{ "userId": "<guid>", "token": string, "newPassword": string }`
- **204.** All of the user's sessions are revoked, so they must log in again.
- **Errors:**
  - 404 `Auth.UserNotFound` (also for a suspended account)
  - 500 `Auth.InvalidPasswordResetToken` (also when `newPassword` fails the server policy: ≥ 8 chars, upper case, lower case, digit). Validate with the registration rules client-side.

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
- ⚠️ This currently also returns doctors that are not approved *(M1)*.

**11. `GET /api/doctors/{id}/slots?date=yyyy-MM-dd`**
- **200:** `SlotResponse[]` with only `Available` slots, ordered by start time.
- An unknown doctor returns `[]`. A missing `date` returns `[]` instead of 400 *(NL11)*, so always send it.
- Slots exist only for the next 14 days.

**12. `GET /api/doctors/{id}/reviews?pageNumber=&pageSize=`**
- **Paging:** default 1/10; `pageSize` 1–20.
- **200:** `ReviewResponse[]`, newest first. An unknown doctor returns `[]`.
- **Errors:** 400 (B)
- Equivalent to #26, which returns 404 for an unknown doctor. **Prefer #26.**

### 5.3 Doctor (self-service)

**13. `PUT /api/doctors/profile`** (Doctor)
- **Body:** `UpdateDoctorProfileRequest`. `consultationFee` is **required** (> 0, ≤ 10 000) *(M15)*. `bio` ≤ 500, `clinicAddress` ≤ 300, `clinicCity` ≤ 100. A `null` text field keeps its current value.
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
- **201:** `ScheduleResponse`. Slots for the next 14 days are generated.
- **Errors:**
  - 400 (B)
  - 404 `Doctor.ProfileNotFound`
  - 500 `Doctor.ScheduleDayAlreadyExists` *(M3: will become 409)*

**16. `PUT /api/doctors/schedule/{id}`** (Doctor, own schedule)
- **Body:** `UpdateScheduleRequest`. All fields are optional; omitted fields keep their value. `slotDurationMinutes` is 10–120. If both times are sent, `endTime` must be after `startTime`.
- **200:** `ScheduleResponse`. Future *available* slots are regenerated. Setting `isActive: false` removes them and generates none.
- **Errors:**
  - 400 (B), or (C) `Doctor.InvalidScheduleTime`
  - 404 `Doctor.ScheduleNotFound` (also when the schedule is another doctor's)
  - ⚠️ 500 when regenerated slots collide with booked ones *(H4)*

**17. `DELETE /api/doctors/schedule/{id}`** (Doctor, own schedule)
- **204**
- **Errors:**
  - 404 `Doctor.ScheduleNotFound`
  - ⚠️ 500 when any slot of the schedule ever had an appointment *(H4)*

### 5.4 Appointments

**18. `POST /api/appointments`** (Patient)
- **Body:** `{ "slotId": int, "reasonForVisit": string | null }`
- **201:** `AppointmentResponse`, with status `Confirmed`. Notifications and a confirmation email are sent.
- **Errors:**
  - 400 (C) `Appointment.SlotInPast`
  - 404 `Patient.ProfileNotFound`, `Appointment.SlotNotFound`
  - 409 `Appointment.SlotNotAvailable`, `Appointment.SlotAlreadyBooked`
  - 500
- **Frontend:** on 409, refresh the slot list.

**19. `GET /api/appointments/my`** (Patient)

| Query | Type | Rule |
|---|---|---|
| `status` | `AppointmentStatus` | Optional filter |
| `pageNumber` | int | **Always send** (≥ 1) *(H6: omitting it returns `[]`; `0` returns 500)* |
| `pageSize` | int | **Always send** (≥ 1, keep ≤ 50) |

- **200:** `AppointmentResponse[]`, newest first.
- **Errors:** 404 `Patient.ProfileNotFound`

**20. `GET /api/appointments/doctor/my`** (Doctor)
- Same query parameters as #19. **Always send `pageNumber` and `pageSize`:** omitted values default to 1 item *(H6)*.
- **200:** `AppointmentResponse[]`, newest first.
- **Errors:** 404 `Doctor.ProfileNotFound`

**21. `GET /api/appointments/{id}`** (owning patient, owning doctor, or admin)
- **200:** `AppointmentResponse`
- **Errors:**
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`

**22. `POST /api/appointments/{id}/cancel`** (Patient, Doctor or Admin; the behaviour depends on the caller's role)
- **Body:** `{ "cancellationReason": string | null }`. **A body is required**; send `{}` if there is no reason.
- **204.** The slot becomes available again, and both parties are notified.
- **Patient rules:**
  - own appointment only
  - not already cancelled or completed
  - at least 2 hours before the start
- **Doctor rules:**
  - own appointment only
  - not cancelled or completed
  - any time
- **Admin rules:**
  - any appointment that is not cancelled or completed
- **Errors:**
  - 400 (C) `Appointment.CancellationWindowExpired` (patient)
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Patient.ProfileNotFound`, `Doctor.ProfileNotFound`
  - 409 `Appointment.AlreadyCancelled`, `Appointment.AlreadyCompleted`

**23. `POST /api/appointments/{id}/complete`** (Doctor, own appointment)
- **204.** The patient can now review the appointment.
- **Errors:**
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Doctor.ProfileNotFound`
  - 409 `Appointment.AlreadyCompleted`
  - 500 `Appointment.CannotCompleteCancelled` *(M3)*
- ⚠️ This can currently be called before the appointment time *(M14)*.

**24. `PUT /api/appointments/{id}/notes`** (Doctor, own appointment)
- **Body:** `{ "notes": string | null }`. This replaces the notes; `null` clears them.
- **204**
- **Errors:**
  - 403 `Appointment.Unauthorized`
  - 404 `Appointment.NotFound`, `Doctor.ProfileNotFound`
  - 500 `Appointment.DoctorNotesNotAllowed` (cancelled) *(M3)*

### 5.5 Reviews

**25. `POST /api/reviews`** (Patient)
- **Body:** `{ "appointmentId": int (>0), "rating": int (1–5), "comment": string | null (≤ 300) }`
- **201:** `ReviewResponse`. The doctor's average rating is updated.
- **Errors:**
  - 400 (B), or (C) `Appointment.ReviewNotAllowed`
  - 403 `Appointment.Unauthorized`
  - 404 `Patient.ProfileNotFound`, `Appointment.NotFound`, `Doctor.NotFound`
  - 409 `Appointment.ReviewAlreadyExists`
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
  - `emergencyContactPhone` matches `^\+?[0-9]{7,15}$`
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
  - `phoneNumber` matches `^\+?[0-9]{7,15}$`
  - `profilePictureUrl` is an absolute URL
  - `dateOfBirth` is in the past
- **204**
- **Errors:**
  - 400 (B)
  - 404 `Profile.NotFound`
  - 500 `Profile.UpdateFailed`

### 5.7 Notifications

**32. `GET /api/notifications/my?unreadOnly=false`**
- **200:** `NotificationResponse[]`, newest first. **The whole inbox, not paged** *(NL2)*.

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
- `includeInactive=true` requires an admin token; anyone else gets 403 (empty body).

**36. `GET /api/Specialty/{id}`**
- **200:** `SpecialtyResponse` (active only)
- **Errors:** 404 `Specialty.NotFound`

**37. `POST /api/Specialty`, `PUT /api/Specialty/{id}`, `DELETE /api/Specialty/{id}`** (Admin)
- **Deprecated.** These are identical to #41–#43. Use the `/api/admin/specialties` routes; these will be removed.

### 5.9 Admin (all endpoints require the Admin role)

**38. `GET /api/admin/doctors/pending`**
- **200:** `DoctorResponse[]`, all doctors awaiting approval (not paged).

**39. `POST /api/admin/doctors/{id}/approve`**
- **204.** The doctor is notified.
- **Errors:**
  - 404 `Doctor.NotFound`
  - 500 `Admin.DoctorAlreadyApproved` *(M3)*

**39b. `POST /api/admin/doctors/{id}/suspend`**
- **204.** The doctor's refresh tokens are revoked; their current access token stays valid for up to 15 min.
- **Errors:**
  - 404 `Doctor.NotFound`
  - 500 `Admin.DoctorAlreadySuspended` *(M3)*

**40. `GET /api/admin/specialties`**
- **200:** `SpecialtyResponse[]`, including inactive specialties.

**41. `POST /api/admin/specialties`**
- **Body:** `SpecialtyRequest`. Rules:
  - `name` is required, ≤ 100, and unique.
  - `description` ≤ 500.
  - `iconUrl` must be an absolute URL.
- **201:** `SpecialtyResponse`
- **Errors:**
  - 400 (B)
  - 500 `Specialty.NameAlreadyExists` *(M3)*

**42. `PUT /api/admin/specialties/{id}`**
- **Body:** `SpecialtyRequest`
- **200:** `SpecialtyResponse`
- **Errors:**
  - 400 (B)
  - 404 `Specialty.NotFound` (also for inactive specialties *(L6)*)
  - 500 `Specialty.NameAlreadyExists`

**43. `DELETE /api/admin/specialties/{id}`**
- **204**
- **Errors:**
  - 404 `Specialty.NotFound`
  - 500 `Specialty.HasAssignedDoctors` *(M3)*

**44. `GET /api/admin/stats/overview`**
- **200:** `OverviewStatsResponse`

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
| `UpdateDoctorProfileRequest` | `bio?: string \| null`, `consultationFee: number`, `clinicAddress?: string \| null`, `clinicCity?: string \| null` |
| `CreateScheduleRequest` | `dayOfWeek: DayOfWeek`, `startTime: TimeOnly`, `endTime: TimeOnly`, `slotDurationMinutes?: number` (default 30) |
| `UpdateScheduleRequest` | `startTime?: TimeOnly`, `endTime?: TimeOnly`, `slotDurationMinutes?: number`, `isActive?: boolean` |
| `BookAppointmentRequest` | `slotId: number`, `reasonForVisit?: string \| null` |
| `CancelAppointmentRequest` | `cancellationReason?: string \| null` |
| `DoctorNotesRequest` | `notes?: string \| null` |
| `SubmitReviewRequest` | `appointmentId: number`, `rating: number`, `comment?: string \| null` |
| `UpdatePatientProfileRequest` | `bloodType?`, `allergies?`, `chronicConditions?`, `emergencyContactName?`, `emergencyContactPhone?` (all nullable) |
| `UpdateUserProfileRequest` | `firstName?`, `lastName?`, `phoneNumber?`, `profilePictureUrl?`, `gender?: Gender`, `dateOfBirth?: DateOnly` (all nullable) |
| `SpecialtyRequest` | `name: string`, `description?: string \| null`, `iconUrl?: string \| null` |

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

`Timestamp` is a UTC date-time string, possibly without the `Z` ([§1](#1-general-conventions)).

## 7. Enums

| Enum | Values |
|---|---|
| `Role` | `Admin`, `Doctor`, `Patient` |
| `AppointmentStatus` | `Pending`, `Confirmed`, `Cancelled`, `Completed`, `NoShow`. Today only `Confirmed`, `Cancelled` and `Completed` occur |
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
| M3 | Expected errors move from 500 to 409 (duplicates, already-approved/suspended, schedule day exists, specialty in use or name taken, email exists) or 400 (bad confirmation/reset token, registration rejected) | Key on the error **code** (`title`), not the status; this already works today |
| H6 | `pageNumber`/`pageSize` get defaults (1/10) and validation (400 for out-of-range values) on appointment lists | Already safe if you always send both |
| M5 + NL9 | All errors, including 401/403/404/500, get a ProblemDetails body; validation errors gain `type` and `traceId` | Handle an empty or a JSON body on any error |
| NM5 | Every endpoint requires authentication unless explicitly public | None, if the frontend only calls endpoints as listed in §4 |
| M13 | New validators for booking, cancel, notes, login, reset password and doctor registration (lengths, ranges) | Some requests accepted today will return 400 (B) |
| M15 | `UpdateDoctorProfileRequest.consultationFee` becomes optional | Can stop always sending the fee |
| L6 | Specialties can be reactivated (`isActive` in `SpecialtyRequest`) | New optional field |
| NL11 | `GET /api/doctors/{id}/slots` without `date` returns 400 | Already safe if you always send `date` |
| NL2 | Paging for notifications and pending doctors, and list responses with a total count | Response shape changes; will be versioned |
| — | Removal of the deprecated `/api/Specialty` write routes (#37) | Use `/api/admin/specialties` |

## 10. Changelog

| Version | Date | Changes |
|---|---|---|
| 1.0.0 | 2026-10-09 | First version of the contract. Behaviour changes that shipped the same day and that the frontend may notice: specialty write routes require Admin (C1); `GET /api/appointments/{id}` returns 403 for non-owners (C4); doctors can no longer delete reviews (H2); refresh fails for suspended users (H3); reuse of a rotated refresh token can revoke all sessions (H7); login lockout after 5 failures with the new code `Auth.AccountLockedOut` (M6); logout only accepts the caller's own refresh token (L7); new code `Appointment.SlotNotFound` replaces `Appointment.NotFound` for an unknown slot (L4); `PUT /api/doctors/schedule/{id}` no longer resets omitted fields (H5); appointment lists now fill `doctorName`/`patientName` (NM4); email links use `Frontend:BaseUrl` |

## 11. How to change this contract

1. **One PR per contract change, in the backend repo**, that updates the code, this file (§4–§7 and the changelog) and `openapi.json` together. Mirror the same text into the frontend repo's copy in a linked PR.
2. **Additive changes** (a new endpoint, a new optional request field, a new response field, a new error code) are a minor version bump (1.**x**.0). The frontend must ignore unknown response fields.
3. **Breaking changes** (removing or renaming a field or route, changing a type, status code, enum value, auth rule or required field) are a major version bump (**x**.0.0). List them in §9 first and agree on a date with the frontend. Ship backward-compatible where possible: add the new form, migrate the frontend, then remove the old form.
4. **Bug fixes that change observed behaviour** (like those in §9) are listed in the changelog even when the code fix is small.
5. **Regenerating `openapi.json`:** run the API in Development and save `GET /openapi/v1.json` (see [SETUP_AND_DEPLOYMENT.md](SETUP_AND_DEPLOYMENT.md#regenerating-openapijson)). Set the `servers` URL to `http://localhost:5143/`.
6. **When the two disagree:** if this document and the running backend differ, the backend is a bug unless the difference is listed in §9. Report it and fix one side in the same week.

## 12. Appendix: TypeScript types

Copy this into the frontend (e.g. `src/api/contract.ts`). Keep it in sync with §6 and §7.

```ts
// ---- scalars ----
export type DateOnly = string;   // "yyyy-MM-dd"
export type TimeOnly = string;   // "HH:mm:ss" (send "HH:mm" or "HH:mm:ss")
export type Timestamp = string;  // UTC; may lack "Z" — normalise before parsing

// ---- enums ----
export type Role = "Admin" | "Doctor" | "Patient";
export type AppointmentStatus = "Pending" | "Confirmed" | "Cancelled" | "Completed" | "NoShow";
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
  title: string;            // error code for business errors, e.g. "Doctor.NotFound"
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
  bio?: string | null; consultationFee: number; clinicAddress?: string | null; clinicCity?: string | null;
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
export interface SpecialtyRequest { name: string; description?: string | null; iconUrl?: string | null; }
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
/** Timestamps from the API are UTC but may lack the "Z" suffix. */
export const parseTimestamp = (value: Timestamp): Date =>
  new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`);
```
