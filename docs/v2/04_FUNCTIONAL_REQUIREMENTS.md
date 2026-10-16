# 04: Functional Requirements

## 1. How to read this document

- **Id:** `FR-<MODULE>-nn`. Ids are never reused; a dropped requirement is marked *Withdrawn*.
- **Priority (MoSCoW):** **M** must have for V2 go-live, **S** should have, **C** could have, **W** won't have in V2 (recorded so it isn't forgotten).
- **Phase:** where it is planned in [10_ROADMAP.md](10_ROADMAP.md).
- **Acceptance criteria** are written to become tests (unit or integration, per [06 §12](06_TECHNICAL_DESIGN.md#12-testing-strategy)).
- Roles and permissions come from [02](02_ROLES_AND_PERMISSIONS.md); entities and statuses from [05](05_DOMAIN_MODEL.md).

## 2. Rules that apply to every module

| # | Rule |
|---|---|
| R1 | Every endpoint requires a permission, except the explicitly public ones (doctor and service search, slots, specialties, auth) |
| R2 | Every clinic-owned record belongs to the organization and, where relevant, a branch. Scope checks use that branch |
| R3 | Every "now" comes from `IDateTimeProvider`; every slot time is branch-local ([ADR 0003](adr/0003-branch-time-zones.md)) |
| R4 | Every list endpoint is paged and returns a total count ([07 §3](07_API_DESIGN.md#3-conventions)) |
| R5 | Every error returns ProblemDetails with a stable error code ([07 §3](07_API_DESIGN.md#3-conventions)) |
| R6 | Every change to a clinical, financial or access-control record writes an audit entry (FR-AUD-01) |
| R7 | Side effects (notifications, emails, SMS) are published as events after the commit and never fail the main operation ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)) |
| R8 | Money is `decimal(18,2)` EGP; no floating point; rounding is half-away-from-zero at line level |
| R9 | Texts entered by staff are stored as-is (Arabic or English); the API returns codes, and the frontend translates |

## 3. Module index

| Code | Module | Phase |
|---|---|---|
| ORG | [Organization and branches](#41-organization-and-branches-org) | P1 |
| STF | [Staff and access](#42-staff-and-access-stf) | P1 |
| SVC | [Services and pricing](#43-services-and-pricing-svc) | P1 |
| SCH | [Scheduling 2.0](#44-scheduling-20-sch) | P2 |
| APT | [Appointments](#45-appointments-apt) | P2 |
| FD | [Front desk and queue](#46-front-desk-and-queue-fd) | P2 |
| EMR | [Clinical records](#47-clinical-records-emr) | P3 |
| BIL | [Billing and payments](#48-billing-and-payments-bil) | P4 |
| INS | [Insurance](#49-insurance-ins) | P5 |
| NTF | [Notifications](#410-notifications-ntf) | P2 |
| RPT | [Reporting](#411-reporting-rpt) | P2–P6 |
| AUD | [Audit](#412-audit-aud) | P0–P1 |
| PAT | [Patient portal](#413-patient-portal-pat) | P2–P5 |

## 4. Requirements

### 4.1 Organization and branches (ORG)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-ORG-01 | Maintain the organization profile: legal name, brand name, logo, tax/commercial registration numbers, contact details | M | P1 | Shown on invoices, receipts and prescriptions; only `org.settings.manage` can edit |
| FR-ORG-02 | Create, edit and deactivate branches: name, code, address, city, geo-location, phone, time zone | M | P1 | A deactivated branch accepts no new bookings; existing records stay readable. Branch code is unique and used in invoice numbers |
| FR-ORG-03 | Branch working hours per weekday and branch holidays (single days or ranges, recurring yearly or not) | M | P1 | Doctor schedules can't create slots outside branch hours or on holidays; adding a holiday lists the affected appointments for rescheduling (J5 worklist) |
| FR-ORG-04 | Rooms per branch: name, type (consultation, procedure, triage), active flag | S | P1 | A room can be reserved by at most one schedule at a time; the queue can assign a patient to a room |
| FR-ORG-05 | Organization settings: patient cancellation window (default 2 h), reschedule window, no-show grace (15 min), no-show fee, deposit policy per service, discount limits per role, refund thresholds, walk-in cap, `clinical.shareWithinBranch` | M | P1 | Each setting has a default; every change is audited; services read settings through one settings service, never constants |
| FR-ORG-06 | Each branch has an IANA time zone (default `Africa/Cairo`) | M | P0 | All "is it in the past?" and reminder calculations use the branch's zone ([ADR 0003](adr/0003-branch-time-zones.md)) |

### 4.2 Staff and access (STF)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-STF-01 | Invite a staff member by email with roles and branch scopes | M | P1 | Invitation link valid 72 h, single use; Managers can't grant `Admin` or `Manager`; resend and revoke are possible |
| FR-STF-02 | Accept an invitation: set a password, confirm the profile | M | P1 | Password policy as registration; the staff member becomes active; the invitation can't be reused |
| FR-STF-03 | Edit a staff member's roles and branch scopes | M | P1 | Takes effect on the next token refresh (≤ 15 min); audited with before/after |
| FR-STF-04 | Deactivate a staff member, immediately or on an end date | M | P1 | Refresh tokens revoked; login blocked; future appointments of a doctor listed for reassignment; authored records keep the author |
| FR-STF-05 | Doctor professional profile: specialty, sub-specialties, license number, bio, languages, photo, services offered, public visibility | M | P1 | Only visible doctors appear in public search; services offered drive which slots are bookable for which service |
| FR-STF-06 | Edit the permissions of a role (organization-level) | C | P1 | Clinical permissions can't be given to `Admin` or `Accountant`; every change audited; a "reset to default" exists |
| FR-STF-07 | MFA (TOTP authenticator app) required for all staff; optional for patients | S | P1 | Staff without MFA can only reach the enrolment screen; Admin can reset a staff member's MFA (audited) |
| FR-STF-08 | Staff directory: search by name, role, branch, status | M | P1 | Paged; respects `staff.view` scope |
| FR-STF-09 | Doctor self-registration is removed | M | P1 | `POST /api/auth/register/doctor` returns 404 in V2 (deprecated in V1 first, [09](09_MIGRATION_FROM_V1.md)) |

### 4.3 Services and pricing (SVC)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-SVC-01 | Service catalog: code, name (AR/EN), category (consultation, follow-up, procedure, package), default duration, specialty, active flag | M | P1 | Codes unique per organization; inactive services can't be booked or billed |
| FR-SVC-02 | Price list items: service + optional branch + optional doctor + price + valid-from/to | M | P1 | Price resolution picks the most specific item valid on the visit date: doctor+branch → doctor → branch → organization. Price history is kept |
| FR-SVC-03 | Doctor–service mapping with optional per-doctor duration | M | P1 | Booking a service only offers doctors mapped to it |
| FR-SVC-04 | Prices are shown to patients when booking | S | P2 | The public slot search returns the resolved price per doctor |
| FR-SVC-05 | External billing codes per service (insurer/TPA codes, CPT-like) | S | P5 | Claims export uses the payer-specific code when present, the internal code otherwise |
| FR-SVC-06 | Follow-up rule: a follow-up within N days of a consultation is free or discounted | C | P4 | Configurable per service; applied automatically when the invoice is built |

### 4.4 Scheduling 2.0 (SCH)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-SCH-01 | Schedules per doctor, per branch, per weekday, with one or more shifts per day, an optional room and a slot length | M | P2 | Replaces V1's one-schedule-per-weekday rule; split shifts (09:00–13:00, 17:00–21:00) are supported; a doctor can't overlap themselves across branches |
| FR-SCH-02 | Slots are generated by a daily job for a rolling window (default 28 days, setting) | M | P2 | Generation is idempotent (never duplicates, never fails on an existing slot); fixes V1 H8 |
| FR-SCH-03 | Changing a schedule never touches booked appointments | M | P2 | Only future `Available` slots are removed or regenerated, in one transaction; booked ones that no longer fit are listed as conflicts for the Manager; fixes V1 H4 |
| FR-SCH-04 | Schedule exceptions: extra hours on a date, or the doctor unavailable for part of a date | S | P2 | Exceptions override the weekly template for that date |
| FR-SCH-05 | Branch holidays block slot generation | M | P2 | See FR-ORG-03 |
| FR-SCH-06 | Leave: a doctor requests, a Manager approves; approval blocks the slots and creates the reschedule worklist | M | P2 | Affected appointments counted at request time; worklist shows each one until it is moved or cancelled (J5) |
| FR-SCH-07 | Block or unblock individual slots with a reason | M | P2 | Blocked slots are not bookable online or at the desk |
| FR-SCH-08 | Controlled overbooking: book a second patient into a slot | S | P2 | Only with `slots.overbook`; capped per slot (setting, default 1 extra); shown clearly in the calendar |
| FR-SCH-09 | Variable visit length: a service longer than one slot books consecutive slots | M | P2 | All needed slots are reserved atomically or none are |
| FR-SCH-10 | Delete a schedule only when it has no future bookings; otherwise deactivate it | M | P2 | Replaces the V1 500 error (H4) with `Schedule.HasFutureBookings` (409) |

### 4.5 Appointments (APT)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-APT-01 | One appointment state machine ([05 §4.1](05_DOMAIN_MODEL.md#41-appointment)); every transition is a method on the entity that returns a `Result` | M | P0 | Illegal transitions return `Appointment.InvalidStatusTransition` (409); fixes V1 M14 |
| FR-APT-02 | Online booking by branch, service, doctor and slot | M | P2 | Atomic (conditional update as in V1); `Pending` while a required deposit is unpaid, otherwise `Confirmed`; booking source recorded (`Online`, `Phone`, `Desk`, `WalkIn`) |
| FR-APT-03 | Booking on behalf of a patient by staff | M | P2 | Requires `appointments.book_on_behalf` in the slot's branch |
| FR-APT-04 | Walk-ins: book into a free slot now, or an unslotted queue appointment | M | P2 | Starts `CheckedIn`; respects the walk-in cap |
| FR-APT-05 | Reschedule: new slot booked and old appointment `Rescheduled` in one transaction, linked both ways | M | P2 | Deposit carried over; patient self-service obeys the reschedule window |
| FR-APT-06 | Cancel with a reason code and free text; by patient (window rule), doctor, desk, manager | M | P2 | Slot returns to `Available` if in the future; deposit refund or retention follows the deposit policy |
| FR-APT-07 | No-show: the desk marks it after the grace period; end-of-day list of unresolved past appointments | M | P2 | No automatic status change; no-show fee applied per policy (FR-BIL-13) |
| FR-APT-08 | Deposits: a service can require a deposit (amount or %) for online bookings, with a hold time | S | P4 | Unpaid at hold expiry → cancelled with `DepositExpired`, slot released |
| FR-APT-09 | Reminders at configurable offsets (default 24 h and 2 h) through the patient's channels | M | P2 | Each reminder sent once (idempotent); failures retried; uses branch time |
| FR-APT-10 | Confirmation request: the patient confirms or cancels from the reminder link | C | P2 | Confirmed state shown to the desk; cancellation follows FR-APT-06 |
| FR-APT-11 | Follow-up booking from the doctor's recommendation | S | P3 | Desk sees "follow-up due" with the suggested date; slots around it are offered |
| FR-APT-12 | Waitlist: a patient asks to be told when an earlier slot opens | C | P6 | When a slot is freed, waitlisted patients are notified in order |

### 4.6 Front desk and queue (FD)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-FD-01 | Patient search by name, phone, MRN, national ID, date of birth | M | P2 | Results in < 1 s for 100k patients; partial phone match; Arabic name search normalises alef/ya/ta-marbuta variants |
| FR-FD-02 | Register a patient at the desk with duplicate detection | M | P2 | Warns on a match of phone, or name + date of birth; creates a `PatientRecord` with a sequential MRN per organization; portal invitation optional |
| FR-FD-03 | Check-in: confirm identity and contact details, show balance and insurance status, create the queue entry and the encounter | M | P2 | Only on the appointment date, within a window (default from 60 min before to the end of the day) |
| FR-FD-04 | Queue board per branch, filterable by doctor: waiting for triage, ready for doctor, in consultation, done; wait time per patient; late arrivals flagged | M | P2 | Updates live (push) without a page refresh |
| FR-FD-05 | Call next / assign room / reorder (with a reason) | M | P2 | Calling moves the appointment to `InProgress`; reorders are audited |
| FR-FD-06 | Waiting-room display: ticket numbers and room, no names | S | P2 | A read-only screen per branch, authenticated with a device token |
| FR-FD-07 | Print visit slip and receipt | S | P4 | Thermal (80 mm) and A4 layouts |
| FR-FD-08 | Merge duplicate patients | S | P2 | Moves appointments, encounters, invoices and policies; keeps an audit trail and a pointer from the merged MRN |

### 4.7 Clinical records (EMR)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-EMR-01 | An encounter is created at check-in for each visit | M | P3 | Exactly one encounter per appointment; walk-ins included |
| FR-EMR-02 | Triage: vital signs (BP, pulse, temperature, respiratory rate, SpO₂, weight, height, BMI computed, pain score), chief complaint, allergy confirmation | M | P3 | Values range-checked with warnings for abnormal values; recorded by and at shown |
| FR-EMR-03 | Doctor notes in SOAP structure with specialty templates | M | P3 | Autosave; templates managed per specialty by the medical director |
| FR-EMR-04 | Diagnoses with ICD-10 codes (search by code or term), primary/secondary, status | M | P3 | ICD-10 table seeded; at least one diagnosis required to sign (setting) |
| FR-EMR-05 | Prescriptions: drug (catalog or free text), form, dose, route, frequency, duration, quantity, instructions; printable and in the portal | M | P3 | Allergy conflict warning must be acknowledged; signed prescriptions are immutable; PDF has a verification code |
| FR-EMR-06 | Patient summary lists: allergies, chronic conditions, current medications, past procedures | M | P3 | Shown on every encounter screen; changes tracked with author and date |
| FR-EMR-07 | Orders and referrals: lab, radiology, specialist referral, with a printable form | S | P3 | Order status (ordered, result received); result linked to an attachment |
| FR-EMR-08 | Attachments: PDFs and images (results, reports, photos), by staff or the patient | M | P3 | Max 20 MB per file; virus-scanned; stored outside the database; access-checked download URLs |
| FR-EMR-09 | Sign an encounter, which locks it | M | P3 | After signing, no edits; only addenda; encounters unsigned after 24 h are flagged |
| FR-EMR-10 | Addenda to signed encounters | M | P3 | Shown in order under the original, each with author and time |
| FR-EMR-11 | Void an encounter entered in error, with the medical director's approval | S | P3 | Hidden from normal views; visible in audit with the reason |
| FR-EMR-12 | Patient history timeline: past encounters, vitals trends, prescriptions, attachments, across branches | M | P3 | Respects `encounters.view` scope; break-glass outside it |
| FR-EMR-13 | Patient-facing visit summary: diagnoses (plain names), prescriptions, orders, instructions; no internal notes | S | P3 | Published to the portal when the encounter is signed |
| FR-EMR-14 | Every read of an encounter or attachment is logged | M | P3 | See FR-AUD-02 |
| FR-EMR-15 | V1 `Appointment.DoctorNotes` is migrated into the encounter | M | P3 | See [09 §2](09_MIGRATION_FROM_V1.md#2-data-migration) |

### 4.8 Billing and payments (BIL)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-BIL-01 | An invoice is created for each visit from the services performed, priced by the price list | M | P4 | Created as `Draft` at check-in or completion (setting); issued at checkout; one visit → one invoice (additional invoices allowed) |
| FR-BIL-02 | Add manual lines (extra services) and remove lines on drafts | M | P4 | Only active services; free-text lines need `invoices.discount`-level permission |
| FR-BIL-03 | Discounts per line or invoice, percentage or amount, with a reason; limits per role; above-limit needs approval | M | P4 | The approval inbox shows requests; issued invoices show the discount and approver |
| FR-BIL-04 | Payer split: each line has a patient share and a payer share | M | P5 | Computed from the coverage rules (FR-INS-06); shares sum to the line total |
| FR-BIL-05 | Payments: cash, card (POS), bank transfer, online, wallet; partial payments; several methods per invoice | M | P4 | Status moves `Issued` → `PartiallyPaid` → `Paid`; overpayment becomes patient credit |
| FR-BIL-06 | Online payments through a payment gateway (deposits and invoices) | S | P4 | No card data touches ClinicFlow; webhook-confirmed; idempotent; gateway refunds supported |
| FR-BIL-07 | Receipts and invoices as PDF with the organization header and a verification code | M | P4 | Reprint marked "copy" |
| FR-BIL-08 | Refunds: requested with reason, approved per threshold, paid by cash or the original method | M | P4 | Requester ≠ approver; cash refunds go through the open cash session |
| FR-BIL-09 | Cash sessions per user per drawer: open with float, close with count, variance, approval above threshold | M | P4 | One open session per user; cash payments need an open session; closed sessions are immutable |
| FR-BIL-10 | Invoice numbering per branch per year, sequential and gapless: `<BRANCH>-<YYYY>-<000001>` | M | P4 | Concurrency-safe; void keeps the number |
| FR-BIL-11 | Void an issued invoice without payments, with approval | M | P4 | Status `Void`; number kept; audited |
| FR-BIL-12 | Patient balance and statement | S | P4 | Balance = issued patient shares − payments + refunds − credits; statement PDF per period |
| FR-BIL-13 | No-show and late-cancellation fees | C | P4 | Per policy: keep the deposit or post a charge to the balance |
| FR-BIL-14 | Deposits are applied to the visit invoice automatically | S | P4 | Any remainder becomes patient credit or is refunded |
| FR-BIL-15 | Daily close report per branch | M | P4 | Visits, invoices, collections by method, refunds, discounts, unbilled completed visits, cash session variances |
| FR-BIL-16 | Egyptian e-receipt / e-invoice integration | W | — | Open question ([10 §5](10_ROADMAP.md#5-open-questions)); invoice data model keeps the needed fields (tax ids, item codes) |

### 4.9 Insurance (INS)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-INS-01 | Payers: insurance companies and TPAs, contact details, claim format, submission deadline (days) | M | P5 | A TPA can administer plans of several insurers |
| FR-INS-02 | Plans and coverage rules: covered services/categories, co-pay (% or fixed), per-visit cap, annual limit, pre-auth required, excluded services, valid dates | M | P5 | Rules versioned by valid dates; the rule in force on the visit date applies |
| FR-INS-03 | Patient policies: payer, plan, member number, holder relationship, valid dates, card images | M | P5 | A patient can have several policies; one is default |
| FR-INS-04 | Eligibility check at check-in, recorded manually (result, reference, who, when) | M | P5 | Check-in warns when a policy is expired or has no eligibility check today |
| FR-INS-05 | Pre-authorizations: request (service, diagnosis, estimated amount), decision (approved amount, number, expiry) | M | P5 | Lines needing pre-auth can't go on a claim without an approved, unexpired one |
| FR-INS-06 | Automatic patient/payer split on invoices | M | P5 | Handles co-pay %, fixed co-pay, caps, exclusions; a manual override needs a reason |
| FR-INS-07 | Claim batches per payer per period with validation (missing codes, pre-auth, expired policy) | M | P5 | Lines with errors are excluded until fixed |
| FR-INS-08 | Claim export per payer format: Excel/CSV/PDF | M | P5 | One export template per payer, configurable columns |
| FR-INS-09 | Claim status tracking with dates and references | M | P5 | State machine in [05 §4.4](05_DOMAIN_MODEL.md#44-claim) |
| FR-INS-10 | Remittance recording: paid / rejected per line with reasons | M | P5 | Updates invoice payer balances; differences visible |
| FR-INS-11 | Denials: resubmit (linked), write off (approval), or transfer to the patient | M | P5 | Every action audited |
| FR-INS-12 | Payer aging and rejection reports | M | P5 | Buckets 0–30, 31–60, 61–90, 90+ days |
| FR-INS-13 | Electronic integration with payers / TPAs | W | — | Per-payer project after V2 |

### 4.10 Notifications (NTF)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-NTF-01 | Channels: in-app (V1), email (V1), SMS, WhatsApp | M | P2 | Providers behind an interface; channel per message type configurable |
| FR-NTF-02 | Templates per event and channel, in Arabic and English, with placeholders | M | P2 | Edited with `notifications.templates`; preview before saving |
| FR-NTF-03 | Events: booking confirmed, reminder, rescheduled, cancelled, no-show, deposit due/expired, invoice issued, payment received, prescription ready, staff invitation | M | P2 | Delivered from the outbox ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)) |
| FR-NTF-04 | Patient preferences: preferred channel and language; opt-out of marketing (no marketing in V2, but the flag exists) | S | P2 | Transactional messages respect the preferred channel |
| FR-NTF-05 | Delivery log with status per message | S | P2 | Visible to Admin; failed messages retried with backoff |
| FR-NTF-06 | Staff notifications: approvals pending, leave approved, unsigned encounters | S | P2 | In-app |

### 4.11 Reporting (RPT)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-RPT-01 | Branch dashboard: today's bookings vs capacity, queue, waits, no-shows, revenue | M | P2 (revenue P4) | Refreshes at least every minute |
| FR-RPT-02 | Utilisation per doctor and branch | M | P6 | Booked minutes / available minutes, by day/week/month |
| FR-RPT-03 | No-show and cancellation analysis | M | P6 | By doctor, service, weekday, booking source, lead time |
| FR-RPT-04 | Wait-time report and live alerts | S | P2 | Median and p90 check-in → in-progress; alert above threshold |
| FR-RPT-05 | Revenue by branch, doctor, service, payer | M | P4 | Gross, discounts, net; patient vs payer share |
| FR-RPT-06 | Collections and patient receivables aging | M | P4 | Buckets 0–30, 31–60, 61–90, 90+ |
| FR-RPT-07 | Cash session report | M | P4 | Per drawer, per user, variance |
| FR-RPT-08 | Claims report | M | P5 | See FR-INS-12 |
| FR-RPT-09 | Clinical quality: unsigned encounters, encounters signed > 24 h, top diagnoses (aggregate) | S | P3 | No patient identities in aggregate reports |
| FR-RPT-10 | Export every report to CSV/Excel | S | P6 | Exports are audited |

### 4.12 Audit (AUD)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-AUD-01 | Audit entry on every create/update/delete of clinical, financial and access-control records: who, when, where (IP), what (before/after), why (reason if required) | M | P0 | Written in the same transaction as the change |
| FR-AUD-02 | Read logging for encounters, attachments, prescriptions and patient demographics | M | P3 | Includes break-glass flag and reason |
| FR-AUD-03 | Audit viewer with filters (user, patient, entity, date, action) | M | P1 | `audit.view`; export audited too |
| FR-AUD-04 | Audit entries are append-only | M | P0 | No API to edit or delete; DB permissions deny UPDATE/DELETE to the app login |

### 4.13 Patient portal (PAT)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-PAT-01 | Book by branch, service and doctor | M | P2 | Search filters: branch, specialty, service, doctor, date |
| FR-PAT-02 | See the price before booking | S | P2 | See FR-SVC-04 |
| FR-PAT-03 | Pay deposits and open invoices online | S | P4 | See FR-BIL-06 |
| FR-PAT-04 | View and download invoices and receipts | S | P4 | Own only |
| FR-PAT-05 | View visit summaries and prescriptions | S | P3 | See FR-EMR-13 |
| FR-PAT-06 | Upload documents (outside results, insurance card) | S | P3 | See FR-EMR-08, FR-INS-03 |
| FR-PAT-07 | Dependants: a guardian manages a child's bookings and records | C | P6 | Relationship verified at the desk |
| FR-PAT-08 | Consent: terms and data processing consent recorded with version and date | M | P2 | Booking blocked until current consent is accepted ([08 §5](08_SECURITY_PRIVACY_COMPLIANCE.md#5-privacy-and-consent)) |
| FR-PAT-09 | Self-service reschedule and cancel | M | P2 | Obeys the windows in FR-ORG-05 |

## 5. Non-functional requirements

| Id | Requirement | Target |
|---|---|---|
| NFR-01 | Availability during clinic hours | 99.5% monthly |
| NFR-02 | API latency | p95 < 300 ms for reads, < 800 ms for writes, excluding external providers |
| NFR-03 | Capacity | 10 branches, 150 staff, 200k patients, 2,000 appointments/day without redesign |
| NFR-04 | Backup and restore | Full daily, log every 15 min; RPO ≤ 15 min, RTO ≤ 4 h; restore tested quarterly |
| NFR-05 | Security | OWASP ASVS level 2 for the API; MFA for staff; TLS 1.2+ everywhere ([08](08_SECURITY_PRIVACY_COMPLIANCE.md)) |
| NFR-06 | Privacy | Compliance checklist in [08 §5](08_SECURITY_PRIVACY_COMPLIANCE.md#5-privacy-and-consent) signed off before go-live |
| NFR-07 | Localisation | Arabic (RTL) and English UI; codes not texts from the API; dates in branch-local time |
| NFR-08 | Accessibility | WCAG 2.1 AA for the patient portal |
| NFR-09 | Observability | Structured logs (Serilog, as V1), health endpoint, metrics for queue length, outbox lag, job failures |
| NFR-10 | Data retention | Clinical and financial records kept per the policy in [08 §6](08_SECURITY_PRIVACY_COMPLIANCE.md#6-retention-and-deletion) |
| NFR-11 | Test coverage | Every state transition, permission rule and money calculation has automated tests |
| NFR-12 | Browser support | Last two versions of Chrome, Edge, Safari, Firefox |
