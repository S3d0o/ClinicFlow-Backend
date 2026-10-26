# 07: API Design (Contract 4.0.0 Direction)

This is the **direction** for the V2 API, not the contract itself. When each phase ships, its endpoints move into `docs/API_CONTRACT.md` 4.x with full request/response tables, following [API_CONTRACT.md §11](../API_CONTRACT.md#11-how-to-change-this-contract).

**Where this document and [API_CONTRACT.md §13](../API_CONTRACT.md#13-planned-apiv2-endpoints-agreed-not-built) differ, §13 wins.** §13 holds the endpoints the V2 frontend already uses, agreed on 2026-10-27 (for example `phone` instead of `phoneNumber`, `otp/send` returning a ticket instead of 204, a two-step MFA result instead of 401 `Auth.MfaRequired`, string ids, and 400/429 instead of 401 for wrong credentials and lockouts). The rest of this document stays the direction for the screens the frontend hasn't built yet.

## 1. Versioning

See [ADR 0005](adr/0005-api-v2-versioning.md) and [ADR 0007](adr/0007-v2-separate-product-line.md).

- V2 endpoints live under **`/api/v2/...`**.
- **V1's API is a separate deployment** from `master`, with its own database. V2 work never changes it.
- The unversioned routes inherited on the `v2` branch serve only the V2 frontend while it is rebuilt. They are not frozen, need no deprecation headers, and are deleted from `v2` as soon as the V2 frontend stops using them, at the latest at V2 GA ([09 §3](09_MIGRATION_FROM_V1.md#3-the-api-on-the-v2-branch)).
- The contract version becomes **4.0.0** when the first `/api/v2` endpoint ships; additive V2 changes after that are 4.x. (V1 already used 2.0.0 for the M3 error-type fix and is at 3.1.2; V1 fixes on `master` stay 3.x.) `docs/API_CONTRACT.md` on the `v2` branch becomes the 4.x contract; the copy on `master` stays V1's 3.x contract.

## 2. Resource map

```
/api/v2
├── auth/…                         register (phone), one-time codes, login by phone or email (+ MFA step),
│                                  refresh, logout, invitations/accept, password flows
├── me                             profile, roles[], branches[], permissions[], language, channels
├── doctor-applications            apply (multipart), my application, withdraw
├── recruitment/applications/{id}  reviewer list, detail, files, interview / accept / reject, preview
├── dashboard                      KPI tiles and the four charts (FR-RPT-11)
├── chat                           chatbot conversation (P7)
├── organization                   profile, settings
├── branches/{branchId}            branch, working hours, holidays, rooms, drawers
│   ├── calendar                   slots + appointments for a date range (staff view)
│   ├── queue                      queue board (+ SignalR hub /hubs/queue)
│   └── cash-sessions              open / close / list
├── staff/{staffId}                directory, invitations, roles, deactivation, doctor profile
├── services, price-list           catalog and prices
├── doctors/{doctorId}             public profile, services, slots; schedules, exceptions, leave (staff)
├── leave-requests/{id}            request / approve / reject, reschedule worklist
├── patients/{patientId}           search, register, demographics, merge, consents, policies, history
├── appointments/{id}              book, reschedule, cancel, check-in, call, complete, no-show
├── encounters/{id}                vitals, notes, diagnoses, prescriptions, orders, addenda, sign, void
├── attachments/{id}               upload, download (streamed, audited)
├── invoices/{id}                  build, lines, discounts, issue, void, pdf
├── payments/{id}                  desk collect, Paymob checkout, webhooks/paymob, refunds
├── approvals/{id}                 inbox: approve / reject
├── payers, plans                  payers, plans, coverage rules
├── pre-authorizations/{id}        request / decide
├── claims/{id}                    batch, validate, export, submit, remittance, resubmit, write-off
├── reports/…                      dashboards and exports
├── audit                          audit log query (Admin, org managers)
├── notifications                  in-app inbox (V1 feature, paged)
└── public/…                       anonymous: branches, specialties, services, doctor search, slots,
                                   document verification
```

## 3. Conventions

| Topic | V2 rule | V1 difference |
|---|---|---|
| Lists | A bare JSON array body, with the total number of matching items in the **`X-Total-Count`** header (`HandlePagedResult`). `pageNumber` / `pageSize`: defaults 1/20, max page size 100; out-of-range → 400 | Same shape as V1; V1 sends `X-Total-Count` on two lists and has per-endpoint limits, V2 on every list with one rule |
| Errors | Always ProblemDetails (RFC 9457): `type`, `title` (error code), `status`, `detail`, `traceId`, and `errors` for validation. 401/403/404/500 included | Same as V1 (M5, NL9 fixed in V1) |
| Validation keys | camelCase property paths (`lines[0].unitPrice`) | V1 uses PascalCase |
| Status codes | Conflicts 409, business-rule input 400, ownership/scope 403, unknown 404; 500 only for real faults | Same as V1 since contract 2.0.0 (M3) |
| Timestamps | UTC ISO 8601 **with `Z`** | V1 omits the `Z` on DB-read values |
| Slot times | Branch-local `date` + `startTime`, plus `startsAtUtc` | V1 returns local times only |
| Money | JSON number with 2 decimals, plus `currency: "EGP"` on documents | — |
| Branch context | In the route for branch resources (`/branches/{branchId}/queue`); as a `branchId` filter on cross-branch lists. No branch header | — |
| Idempotency | `Idempotency-Key` header **required** on `POST` bookings, payments and refunds | — |
| Concurrency | Entities that can be edited concurrently return `rowVersion`; updates send it back; mismatch → 409 `Concurrency.Conflict` | — |
| Actions | State changes are explicit sub-resources: `POST /appointments/{id}/check-in`, not `PATCH status` | Same style as V1 `cancel`/`complete` |
| Enums | Strings (V1); new values are additive and listed in the changelog | — |
| Partial updates | `PATCH` with only the sent fields; nullable value types fixed (V1 M15) | — |
| Localisation | The API returns codes and stored texts; the client translates codes. Bilingual reference data is returned with both names (`nameAr`, `nameEn`). Emails and phone messages use the recipient's stored `preferredLanguage`; `Accept-Language` only selects the language of PDFs generated for the caller | V1 sends English emails only |
| Phone numbers | Accepted as `01xxxxxxxxx` or `+201xxxxxxxxx`; always returned as E.164 (`+201…`) | V1 stores the number as typed |
| Files | Uploads are `multipart/form-data`; downloads stream through an access-checked endpoint, never a public URL | — |

### Error code style

`<Module>.<Reason>`, as in V1. Examples of new codes:

| Code | Status | When |
|---|---|---|
| `Auth.MfaRequired` | 401 | Password accepted, MFA code needed (body carries the MFA token) |
| `Auth.InvalidMfaCode` | 401 | Wrong TOTP code |
| `Auth.PhoneNotVerified` | 403 | Booking or applying before the phone is verified |
| `Auth.PhoneAlreadyUsed` | 409 | Changing to a number that belongs to another account (sign-up returns a generic response instead) |
| `Otp.InvalidCode` | 400 | Wrong or expired code |
| `Otp.TooManyAttempts` | 429 | 5 wrong attempts on one code, or the daily cap reached |
| `Otp.ResendTooSoon` | 429 | A new code requested within 60 s |
| `DoctorApplication.InvalidStatusTransition` | 409 | Stage move not allowed from the current status |
| `DoctorApplication.AlreadyOpen` | 409 | The user already has an `Applied` or `Interview` application |
| `DoctorApplication.CvRequired` | 400 | Applying without a CV |
| `File.TypeNotAllowed` / `File.TooLarge` | 400 | Upload fails the type or size check |
| `File.NotScanned` | 409 | Downloading a file whose virus scan hasn't passed |
| `Payment.GatewayUnavailable` | 503 | Paymob can't be reached when creating a checkout; the booking itself is unaffected |
| `Access.OutOfScope` | 403 | The record's branch is outside the user's scope for this permission |
| `Concurrency.Conflict` | 409 | `rowVersion` mismatch |
| `Idempotency.KeyReused` | 409 | Same key, different request body |
| `Appointment.InvalidStatusTransition` | 409 | Transition not allowed from the current status |
| `Appointment.CheckInWindowClosed` | 400 | Check-in outside the allowed window |
| `Schedule.HasFutureBookings` | 409 | Deleting a schedule with future appointments |
| `Schedule.OverlapsOtherBranch` | 409 | Doctor already scheduled elsewhere at that time |
| `Slot.CapacityReached` | 409 | Slot full (replaces `Appointment.SlotAlreadyBooked`) |
| `Encounter.AlreadySigned` | 409 | Editing a signed encounter |
| `Prescription.AllergyConflict` | 400 | Allergy conflict not acknowledged |
| `Invoice.NotDraft` | 409 | Changing lines of an issued invoice |
| `Invoice.DiscountAboveLimit` | 403 | Discount above the user's limit without approval |
| `Payment.NoOpenCashSession` | 409 | Cash payment without an open session |
| `Refund.ExceedsRefundable` | 400 | Amount above what can be refunded |
| `Approval.SelfApprovalNotAllowed` | 403 | Requester tried to approve their own request |
| `Claim.LineValidationFailed` | 400 | Claim lines missing codes or pre-auth |
| `PreAuth.Required` | 409 | A service needing pre-auth was put on a claim without one |

## 4. Key endpoints per module

Each row is `METHOD path` → permission. "public" = anonymous.

### Auth and me

| Endpoint | Permission | Notes |
|---|---|---|
| `POST /api/v2/auth/register` | public; rate-limited | `{ firstName, lastName, phoneNumber, email?, password, preferredLanguage, ... }` → sends a one-time code (WhatsApp, or SMS when on); the account is usable after `/otp/verify` (FR-ACC-01) |
| `POST /api/v2/auth/otp/send` | public; `Otp` rate limit | `{ phoneNumber, purpose }` (`SignUp`, `ChangePhone`, `PasswordReset`); always 204, whether or not the number exists |
| `POST /api/v2/auth/otp/verify` | public; `Otp` rate limit | `{ phoneNumber, purpose, code }` → for `SignUp`: marks the phone verified and returns tokens |
| `POST /api/v2/auth/password/reset-by-otp` | public; `Otp` rate limit | `{ phoneNumber, code, newPassword }`; revokes all refresh tokens |
| `POST /api/v2/auth/login` | public | `{ identifier, password }`, where `identifier` is a phone or an email. Returns tokens, or 401 `Auth.MfaRequired` with `mfaToken` |
| `POST /api/v2/auth/mfa` | public (MFA token) | `{ mfaToken, code }` → tokens |
| `POST /api/v2/auth/mfa/enroll`, `/mfa/confirm` | authenticated | TOTP setup, recovery codes |
| `POST /api/v2/auth/invitations/accept` | public (invitation token) | Set password, activate staff |
| `POST /api/v2/auth/refresh-token`, `/logout` | as V1 | Unchanged rules ([API_CONTRACT §2.3](../API_CONTRACT.md#23-refresh-rules-frontend-must-follow-these)) |
| `GET /api/v2/me` | authenticated | `{ user, phoneVerified, emailConfirmed, preferredLanguage, notificationChannel, roles[], branches[], permissions[{ name, scope, branchIds[] }], isMedicalDirector, hasDoctorApplication }`; the frontend builds its menus from `permissions` |
| `PATCH /api/v2/me` | authenticated | `preferredLanguage`, `notificationChannel` (`Phone`, `Email`, `Both`; `Email`/`Both` need a confirmed email) |
| `POST /api/v2/me/phone`, `POST /api/v2/me/email` | authenticated | Change the phone (a one-time code to the new number) or add an email (confirmation link) (FR-ACC-05) |

### Recruitment

| Endpoint | Permission | Notes |
|---|---|---|
| `POST /api/v2/doctor-applications` | authenticated, phone and email verified | `multipart/form-data`: `specialtyId`, `licenseNo`, `yearsOfExperience`, `preferredBranchId?`, `cv` (file, required), `coverLetter` (file, optional) or `coverLetterText` → 201 |
| `GET /api/v2/doctor-applications/mine` | authenticated (own) | Current application with stage history and interview details |
| `POST /api/v2/doctor-applications/mine/withdraw` | authenticated (own) | Before acceptance only |
| `GET /api/v2/recruitment/applications?status=&specialtyId=&branchId=&from=&to=` | `recruitment.view` | Paged, `X-Total-Count` |
| `GET /api/v2/recruitment/applications/{id}` | `recruitment.view` | Detail and full history |
| `GET /api/v2/recruitment/applications/{id}/files/{fileId}` | `recruitment.view` | Streams the CV or cover letter; audited |
| `POST /api/v2/recruitment/applications/{id}/preview-message` | `recruitment.manage` | `{ toStatus, variables }` → the rendered email and phone message in the applicant's language, nothing saved |
| `POST /api/v2/recruitment/applications/{id}/interview` | `recruitment.manage` | `{ interviewAt, branchId?, location?, interviewerName, notes?, rowVersion }` |
| `POST /api/v2/recruitment/applications/{id}/accept` | `recruitment.manage` | `{ startDate, branchId, notes?, rowVersion }`; creates the staff member |
| `POST /api/v2/recruitment/applications/{id}/reject` | `recruitment.manage` | `{ message?, rowVersion }` |

### Organization, branches, staff, catalog

| Endpoint | Permission |
|---|---|
| `GET/PUT /api/v2/organization`, `GET/PUT /api/v2/organization/settings` | `org.settings.manage` (GET settings: any staff) |
| `GET/POST /api/v2/branches`, `GET/PUT /api/v2/branches/{id}`, `POST .../deactivate` | `branches.manage` (GET: any staff) |
| `GET/PUT /api/v2/branches/{id}/working-hours`, `GET/POST/DELETE .../holidays` | `branches.manage` |
| `GET/POST/PUT /api/v2/branches/{id}/rooms` | `rooms.manage` |
| `GET /api/v2/staff`, `GET /api/v2/staff/{id}` | `staff.view` |
| `POST /api/v2/staff/invitations`, `POST .../{id}/resend`, `DELETE .../{id}` | `staff.invite` |
| `PUT /api/v2/staff/{id}/roles` | `staff.invite` |
| `POST /api/v2/staff/{id}/deactivate` | `staff.deactivate` |
| `PUT /api/v2/staff/{id}/doctor-profile` | `staff.invite` or own (`schedules.manage_own`) for bio/photo |
| `GET/PUT /api/v2/roles/{role}/permissions` | `roles.manage` |
| `GET/POST/PUT /api/v2/services`, `GET/POST/PUT /api/v2/price-list` | `services.manage`, `prices.manage` |

### Scheduling, appointments, front desk

| Endpoint | Permission |
|---|---|
| `GET/POST/PUT/DELETE /api/v2/doctors/{id}/schedules[/{scheduleId}]` | `schedules.manage` / `schedules.manage_own` |
| `GET/POST /api/v2/doctors/{id}/exceptions` | `schedules.manage` |
| `POST /api/v2/leave-requests`, `POST .../{id}/approve`, `.../reject`, `GET .../{id}/worklist` | `leave.request`, `leave.approve` |
| `POST /api/v2/slots/{id}/block`, `.../unblock` | `slots.block` |
| `GET /api/v2/branches/{id}/calendar?from=&to=&doctorId=` | `appointments.view` |
| `GET /api/v2/public/slots?branchId=&serviceId=&doctorId=&date=` | public (price included) |
| `POST /api/v2/appointments` | patient (own, verified phone) or `appointments.book_on_behalf`; `Idempotency-Key`. Always returns a `Confirmed` appointment with the optional `depositAmount` |
| `POST /api/v2/appointments/{id}/deposit` | patient (own); `Idempotency-Key` | Creates a Paymob payment intention for the optional deposit → `{ checkoutUrl }` |
| `POST /api/v2/appointments/walk-in` | `appointments.check_in` |
| `POST /api/v2/appointments/{id}/reschedule` | patient (window) or `appointments.reschedule` |
| `POST /api/v2/appointments/{id}/cancel` | patient (window), doctor (own), `appointments.cancel` |
| `POST /api/v2/appointments/{id}/check-in` | `appointments.check_in` |
| `POST /api/v2/appointments/{id}/call` | doctor (own), `queue.manage` |
| `POST /api/v2/appointments/{id}/complete` | doctor (own) |
| `POST /api/v2/appointments/{id}/no-show`, `.../no-show/revert` | `appointments.mark_no_show`, Manager |
| `GET /api/v2/branches/{id}/queue?doctorId=` | `queue.view`; live updates on `/hubs/queue` |
| `PUT /api/v2/branches/{id}/queue/order` | `queue.manage` |

### Patients and clinical

| Endpoint | Permission |
|---|---|
| `GET /api/v2/patients?q=` | `patients.view` |
| `POST /api/v2/patients` | `patients.register` |
| `GET/PATCH /api/v2/patients/{id}` | `patients.view` / `patients.edit` |
| `POST /api/v2/patients/{id}/merge-into/{targetId}` | `patients.merge` (approval) |
| `GET /api/v2/patients/{id}/history` | `encounters.view` (scope or break-glass) |
| `POST /api/v2/patients/{id}/break-glass` | `records.break_glass` (reason required) |
| `GET/POST/PATCH /api/v2/patients/{id}/allergies`, `/conditions`, `/medications` | `encounters.write` or `vitals.write` (allergies) |
| `GET /api/v2/encounters/{id}` | `encounters.view` |
| `POST /api/v2/encounters/{id}/vitals` | `vitals.write` |
| `PUT /api/v2/encounters/{id}/notes` | `encounters.write` (own, `Draft`; `rowVersion`) |
| `POST/DELETE /api/v2/encounters/{id}/diagnoses[/{dxId}]` | `encounters.write` |
| `POST /api/v2/encounters/{id}/prescriptions`, `GET /api/v2/prescriptions/{id}/pdf` | `prescriptions.write` / view |
| `POST /api/v2/encounters/{id}/orders` | `encounters.write` |
| `POST /api/v2/encounters/{id}/sign`, `.../addenda`, `.../void` | `encounters.sign`, `encounters.addend`, `encounters.void` |
| `POST /api/v2/attachments` (multipart), `GET /api/v2/attachments/{id}/content` | `attachments.upload`, `encounters.view` |
| `GET /api/v2/icd10?q=` | any staff |

### Billing and insurance

| Endpoint | Permission |
|---|---|
| `GET /api/v2/invoices?branchId=&status=&patientId=&from=&to=` | `invoices.view` |
| `POST /api/v2/invoices`, `POST/DELETE .../{id}/lines[/{lineId}]` | `invoices.create` |
| `POST /api/v2/invoices/{id}/discounts` | `invoices.discount` (limit, else approval request) |
| `POST /api/v2/invoices/{id}/issue`, `.../void`, `GET .../{id}/pdf` | `invoices.create`, `invoices.void`, `invoices.view` |
| `GET /api/v2/appointments/{id}/amount-due` | `payments.collect` | Price, paid deposit and the remainder the desk collects |
| `POST /api/v2/payments` | `payments.collect` (the receptionist at the desk: cash or card, in their open cash session); `Idempotency-Key` |
| `POST /api/v2/payments/online-checkout` | patient (own) or staff; Paymob checkout for an open invoice → `{ checkoutUrl }` |
| `POST /api/v2/payments/webhooks/paymob` | public (Paymob HMAC) |
| `POST /api/v2/payments/{id}/refunds` | `refunds.request`; `Idempotency-Key` |
| `GET /api/v2/approvals?status=pending`, `POST .../{id}/approve`, `.../reject` | per approval type |
| `POST /api/v2/branches/{id}/cash-sessions`, `POST .../cash-sessions/{sid}/close`, `.../approve` | `cash_sessions.operate`, `cash_sessions.reconcile` |
| `GET/POST/PUT /api/v2/payers`, `/plans`, `/plans/{id}/coverage-rules` | `payers.manage` |
| `GET/POST/PUT /api/v2/patients/{id}/policies`, `POST .../policies/{pid}/eligibility-checks` | `policies.manage` |
| `POST /api/v2/pre-authorizations`, `POST .../{id}/decision` | `preauth.manage` |
| `POST /api/v2/claims` (batch), `GET .../{id}/validation`, `GET .../{id}/export?format=xlsx`, `POST .../{id}/submit`, `POST .../{id}/remittance`, `POST /api/v2/claim-lines/{id}/resubmit`, `.../write-off` | `claims.manage` |

### Reports, audit, public

| Endpoint | Permission |
|---|---|
| `GET /api/v2/dashboard?branchId=` | `reports.operational` (revenue parts need `reports.financial`). Returns `{ tiles, appointmentsByDay[], noShowRateByWeek[], revenueByDay[], visitsByDoctor[] }`: ready-made series (FR-RPT-11) |
| `POST /api/v2/chat/messages` | patient (own); P7 | Chatbot turn (FR-BOT-01); rate-limited |
| `GET /api/v2/reports/{reportKey}?branchId=&from=&to=&format=json\|csv\|xlsx` | per report category |
| `GET /api/v2/audit?userId=&patientId=&entityType=&from=&to=` | `audit.view` |
| `GET /api/v2/public/branches`, `/public/services`, `/public/doctors`, `/public/specialties` | public |
| `GET /api/v2/public/documents/verify/{code}` | public; minimal data only |

## 5. Differences from V1

V1 clients keep using the V1 deployment, so these aren't breaking changes for them. They are what the V2 frontend has to handle differently from the V1 code it starts from.

| V1 | V2 | Frontend impact |
|---|---|---|
| PascalCase validation keys | camelCase keys | Field mapping changes |
| `POST /api/auth/register` with a required email | `POST /api/v2/auth/register` with a required phone and a one-time code; email optional | New sign-up screen with a code step |
| Login by email | Login by phone or email (`identifier`) | Login form accepts either |
| `POST /api/auth/register/doctor`, `/api/admin/doctors/{id}/approve` | Doctor applications (`/api/v2/doctor-applications`, `/api/v2/recruitment/...`) | Doctor sign-up becomes an application form; approval becomes the recruitment screens |
| `UserProfileResponse.role` (single) | `GET /api/v2/me` with `roles[]` and `permissions[]` | Menus driven by permissions |
| `AppointmentResponse` without branch/service | Adds `branch`, `service`, `source`, `queueTicket`, `startsAtUtc`; new statuses | Status badges for the new values |
| `DoctorNotes` on the appointment | Encounters | Doctor screens move to the encounter UI |
| `consultationFee` on the doctor | Price per service from the price list | Booking screens show service prices |
| Timestamps without `Z` | With `Z` | `parseTimestamp` helper becomes a no-op |
| `/api/Specialty` write routes (deprecated) | Removed | Already planned in V1 §9 |
| Patient lists of appointments by user | `GET /api/v2/me/appointments` paged, filtered | — |

## 6. Real-time and webhooks

| Channel | Purpose | Auth |
|---|---|---|
| SignalR `/hubs/queue` | Queue board updates per branch | Access token (`queue.view`) or display device token |
| SignalR `/hubs/notifications` | Live in-app notifications (replaces polling) | Access token |
| `POST /api/v2/payments/webhooks/paymob` | Paymob payment confirmations and refunds | Paymob HMAC |
| `POST /api/v2/messaging/webhooks/{provider}` | WhatsApp/SMS delivery receipts and replies (confirm/cancel links) | Provider signature |
