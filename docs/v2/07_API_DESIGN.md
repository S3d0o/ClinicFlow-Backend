# 07: API Design (Contract 2.0.0 Direction)

This is the **direction** for the V2 API, not the contract itself. When each phase ships, its endpoints move into `docs/API_CONTRACT.md` 2.x with full request/response tables, following [API_CONTRACT.md §11](../API_CONTRACT.md#11-how-to-change-this-contract).

## 1. Versioning

See [ADR 0005](adr/0005-api-v2-versioning.md).

- V2 endpoints live under **`/api/v2/...`**.
- The unversioned V1 routes (`/api/...`) are **frozen**: bug and security fixes only. They keep working until the frontend has moved every screen to V2, then they are removed, with dates agreed in the contract's §9.
- V1 and V2 share the database and the services. V1 controllers become thin adapters over V2 services where behaviour still matches; V1 endpoints whose concept no longer exists (doctor self-registration, admin doctor approval) are deprecated first and removed at the end of the window.
- The contract version becomes **2.0.0** when the first `/api/v2` endpoint ships; additive V2 changes after that are 2.x.

## 2. Resource map

```
/api/v2
├── auth/…                         login (+ MFA step), refresh, logout, invitations/accept, password flows
├── me                             profile, roles[], branches[], permissions[], preferences
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
├── payments/{id}                  collect, online checkout, webhooks/{provider}, refunds
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
| Lists | `PagedResponse<T>`: `{ "items": [...], "pageNumber": 1, "pageSize": 20, "totalCount": 134 }`. Defaults 1/20, max page size 100; out-of-range → 400 | V1 returns bare arrays with no count (NL2, H6) |
| Errors | Always ProblemDetails (RFC 9457): `type`, `title` (error code), `status`, `detail`, `traceId`, and `errors` for validation. 401/403/404/500 included | V1 has empty bodies for middleware errors (M5, NL9) |
| Validation keys | camelCase property paths (`lines[0].unitPrice`) | V1 uses PascalCase |
| Status codes | Conflicts 409, business-rule input 400, ownership/scope 403, unknown 404; 500 only for real faults | V1 returns 500 for several expected errors (M3) |
| Timestamps | UTC ISO 8601 **with `Z`** | V1 omits the `Z` on DB-read values |
| Slot times | Branch-local `date` + `startTime`, plus `startsAtUtc` | V1 returns local times only |
| Money | JSON number with 2 decimals, plus `currency: "EGP"` on documents | — |
| Branch context | In the route for branch resources (`/branches/{branchId}/queue`); as a `branchId` filter on cross-branch lists. No branch header | — |
| Idempotency | `Idempotency-Key` header **required** on `POST` bookings, payments and refunds | — |
| Concurrency | Entities that can be edited concurrently return `rowVersion`; updates send it back; mismatch → 409 `Concurrency.Conflict` | — |
| Actions | State changes are explicit sub-resources: `POST /appointments/{id}/check-in`, not `PATCH status` | Same style as V1 `cancel`/`complete` |
| Enums | Strings (V1); new values are additive and listed in the changelog | — |
| Partial updates | `PATCH` with only the sent fields; nullable value types fixed (V1 M15) | — |
| Localisation | The API returns codes and stored texts; the client translates codes. `Accept-Language` only selects the language of generated PDFs and messages | — |

### Error code style

`<Module>.<Reason>`, as in V1. Examples of new codes:

| Code | Status | When |
|---|---|---|
| `Auth.MfaRequired` | 401 | Password accepted, MFA code needed (body carries the MFA token) |
| `Auth.InvalidMfaCode` | 401 | Wrong TOTP code |
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
| `POST /api/v2/auth/login` | public | Returns tokens, or 401 `Auth.MfaRequired` with `mfaToken` |
| `POST /api/v2/auth/mfa` | public (MFA token) | `{ mfaToken, code }` → tokens |
| `POST /api/v2/auth/mfa/enroll`, `/mfa/confirm` | authenticated | TOTP setup, recovery codes |
| `POST /api/v2/auth/invitations/accept` | public (invitation token) | Set password, activate staff |
| `POST /api/v2/auth/refresh-token`, `/logout` | as V1 | Unchanged rules ([API_CONTRACT §2.3](../API_CONTRACT.md#23-refresh-rules-frontend-must-follow-these)) |
| `GET /api/v2/me` | authenticated | `{ user, roles[], branches[], permissions[{ name, scope, branchIds[] }], isMedicalDirector }`; the frontend builds its menus from `permissions` |

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
| `POST /api/v2/appointments` | patient (own) or `appointments.book_on_behalf`; `Idempotency-Key` |
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
| `POST /api/v2/payments` | `payments.collect`; `Idempotency-Key` |
| `POST /api/v2/payments/online-checkout` | patient (own) or staff; returns the gateway redirect |
| `POST /api/v2/payments/webhooks/{provider}` | public (signature) |
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
| `GET /api/v2/reports/dashboard?branchId=` | `reports.operational` |
| `GET /api/v2/reports/{reportKey}?branchId=&from=&to=&format=json\|csv\|xlsx` | per report category |
| `GET /api/v2/audit?userId=&patientId=&entityType=&from=&to=` | `audit.view` |
| `GET /api/v2/public/branches`, `/public/services`, `/public/doctors`, `/public/specialties` | public |
| `GET /api/v2/public/documents/verify/{code}` | public; minimal data only |

## 5. Breaking changes from V1

Listed in `API_CONTRACT.md` §9 before they ship.

| V1 | V2 | Frontend impact |
|---|---|---|
| Bare arrays on list endpoints | `PagedResponse<T>` | Every list screen reads `items` and shows paging |
| Empty 401/403/404/500 bodies; PascalCase validation keys | ProblemDetails everywhere; camelCase keys | Error handler simplifies; field mapping changes |
| 500 for expected errors (M3) | 409 / 400 | Key on codes (already recommended) |
| `POST /api/auth/register/doctor`, `/api/admin/doctors/{id}/approve` | Removed; staff invitations | Remove the doctor sign-up and approval screens |
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
| `POST /api/v2/payments/webhooks/{provider}` | Payment confirmations and refunds | Provider signature |
| `POST /api/v2/messaging/webhooks/{provider}` | SMS/WhatsApp delivery receipts and replies (confirm/cancel links) | Provider signature |
