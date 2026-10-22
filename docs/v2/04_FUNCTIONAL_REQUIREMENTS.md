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
| R3 | Every "now" comes from `IClock` (which extends V1's `IDateTimeProvider`); every slot time is branch-local ([ADR 0003](adr/0003-branch-time-zones.md)) |
| R4 | Every list endpoint is paged: the body is a JSON array and the total count is in the `X-Total-Count` header, as in V1 ([07 §3](07_API_DESIGN.md#3-conventions)) |
| R5 | Every error returns ProblemDetails with a stable error code ([07 §3](07_API_DESIGN.md#3-conventions)) |
| R6 | Every change to a clinical, financial or access-control record writes an audit entry (FR-AUD-01) |
| R7 | Side effects (notifications, emails, SMS) are published as events after the commit and never fail the main operation ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)) |
| R8 | Money is `decimal(18,2)` EGP; no floating point; rounding is half-away-from-zero at line level |
| R9 | Texts entered by staff are stored as-is (Arabic or English); the API returns codes, and the frontend translates. Reference data shown to patients has both names: `NameAr` and `NameEn` on specialties, services, branches and rooms; a doctor's bio may be written in either or both |
| R10 | Every email and SMS is rendered by the backend in the recipient's preferred language (FR-ACC-06), from AR/EN templates (FR-NTF-02) |

## 3. Module index

| Code | Module | Phase |
|---|---|---|
| ORG | [Organization and branches](#41-organization-and-branches-org) | P1 |
| STF | [Staff and access](#42-staff-and-access-stf) | P1 |
| REC | [Recruitment](#414-recruitment-rec) | P1 |
| ACC | [Accounts and sign-in](#415-accounts-and-sign-in-acc) | P1 |
| SVC | [Services and pricing](#43-services-and-pricing-svc) | P1 |
| SCH | [Scheduling 2.0](#44-scheduling-20-sch) | P2 |
| APT | [Appointments](#45-appointments-apt) | P2 |
| FD | [Front desk and queue](#46-front-desk-and-queue-fd) | P2 |
| BIL | [Billing and payments](#48-billing-and-payments-bil) | P3 |
| EMR | [Clinical records](#47-clinical-records-emr) | P4 |
| INS | [Insurance](#49-insurance-ins) | P5 |
| NTF | [Notifications](#410-notifications-ntf) | P2 |
| RPT | [Reporting](#411-reporting-rpt) | P2–P6 |
| AUD | [Audit](#412-audit-aud) | P0–P1 |
| PAT | [Patient portal](#413-patient-portal-pat) | P2–P6 |
| BOT | [Chatbot](#416-chatbot-bot) | P7 |

Section numbers below follow the order the modules were added, not the phase order.

## 4. Requirements

### 4.1 Organization and branches (ORG)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-ORG-01 | Maintain the organization profile: legal name, brand name, logo, tax/commercial registration numbers, contact details | M | P1 | Shown on invoices, receipts and prescriptions; only `org.settings.manage` can edit |
| FR-ORG-02 | Create, edit and deactivate branches: name, code, address, city, geo-location, phone, time zone | M | P1 | A deactivated branch accepts no new bookings; existing records stay readable. Branch code is unique and used in invoice numbers |
| FR-ORG-03 | Branch working hours per weekday and branch holidays (single days or ranges, recurring yearly or not) | M | P1 | Doctor schedules can't create slots outside branch hours or on holidays; adding a holiday lists the affected appointments for rescheduling (J5 worklist) |
| FR-ORG-04 | Rooms per branch: name, type (consultation, procedure, triage), active flag | S | P1 | A room can be reserved by at most one schedule at a time; the queue can assign a patient to a room |
| FR-ORG-05 | Organization settings: patient cancellation window (default 2 h), reschedule window, no-show grace (15 min), no-show fee, optional deposit amount per service and its refund rule, rejected-application file retention (default 6 months), discount limits per role, refund thresholds, walk-in cap, `clinical.shareWithinBranch` | M | P1 | Each setting has a default; every change is audited; services read settings through one settings service, never constants |
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
| FR-STF-09 | V1 doctor self-registration and admin approval are replaced by the doctor application pipeline ([§4.14](#414-recruitment-rec)) | M | P1 | `POST /api/auth/register/doctor` and the V1 approve/suspend endpoints are deleted from the `v2` branch at the end of P1 ([09 §3](09_MIGRATION_FROM_V1.md#3-the-api-on-the-v2-branch)); V1 keeps them; doctors join by application (FR-REC-01) or invitation (FR-STF-01) |

### 4.3 Services and pricing (SVC)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-SVC-01 | Service catalog: code, name (AR/EN), category (consultation, follow-up, procedure, package), default duration, specialty, active flag | M | P1 | Codes unique per organization; inactive services can't be booked or billed |
| FR-SVC-02 | Price list items: service + optional branch + optional doctor + price + valid-from/to | M | P1 | Price resolution picks the most specific item valid on the visit date: doctor+branch → doctor → branch → organization. Price history is kept |
| FR-SVC-03 | Doctor–service mapping with optional per-doctor duration | M | P1 | Booking a service only offers doctors mapped to it |
| FR-SVC-04 | Prices are shown to patients when booking | S | P2 | The public slot search returns the resolved price per doctor |
| FR-SVC-05 | External billing codes per service (insurer/TPA codes, CPT-like) | S | P5 | Claims export uses the payer-specific code when present, the internal code otherwise |
| FR-SVC-06 | Follow-up rule: a follow-up within N days of a consultation is free or discounted | C | P3 | Configurable per service; applied automatically when the invoice is built |

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
| FR-APT-02 | Online booking by branch, service, doctor and slot | M | P2 | Atomic (conditional update as in V1); the appointment is **`Confirmed` at once**, whether or not a deposit is paid; requires a verified phone (FR-ACC-02); booking source recorded (`Online`, `Phone`, `Desk`, `WalkIn`) |
| FR-APT-03 | Booking on behalf of a patient by staff | M | P2 | Requires `appointments.book_on_behalf` in the slot's branch |
| FR-APT-04 | Walk-ins: book into a free slot now, or an unslotted queue appointment | M | P2 | Starts `CheckedIn`; respects the walk-in cap |
| FR-APT-05 | Reschedule: new slot booked and old appointment `Rescheduled` in one transaction, linked both ways | M | P2 | Deposit carried over; patient self-service obeys the reschedule window |
| FR-APT-06 | Cancel with a reason code and free text; by patient (window rule), doctor, desk, manager | M | P2 | Slot returns to `Available` if in the future; deposit refund or retention follows the deposit policy |
| FR-APT-07 | No-show: the desk marks it after the grace period; end-of-day list of unresolved past appointments | M | P2 | No automatic status change; no-show fee applied per policy (FR-BIL-13) |
| FR-APT-08 | **Optional deposit** through Paymob: when booking online, or later from the appointment, the patient **may** prepay a deposit (amount per service, a setting). It is never required | S | P3 | The booking stays `Confirmed` whether or not a deposit is paid (no hold time, no expiry); a paid deposit is shown to the desk and deducted at checkout (FR-BIL-14); on cancellation it is refunded or kept per the deposit policy (FR-APT-06) |
| FR-APT-09 | Booking confirmation and reminders at configurable offsets (default 24 h and 2 h) by the patient's chosen channel: SMS, email or both (FR-NTF-04) | M | P2 | Each reminder sent once per channel (idempotent); failures retried; uses branch time; text in the patient's language |
| FR-APT-10 | Confirmation request: the patient confirms or cancels from the reminder link | C | P2 | Confirmed state shown to the desk; cancellation follows FR-APT-06 |
| FR-APT-11 | Follow-up booking from the doctor's recommendation | S | P4 | Desk sees "follow-up due" with the suggested date; slots around it are offered |
| FR-APT-12 | Waitlist: a patient asks to be told when an earlier slot opens | C | P6 | When a slot is freed, waitlisted patients are notified in order |

### 4.6 Front desk and queue (FD)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-FD-01 | Patient search by name, phone, MRN, national ID, date of birth | M | P2 | Results in < 1 s for 100k patients; partial phone match; Arabic name search normalises alef/ya/ta-marbuta variants |
| FR-FD-02 | Register a patient at the desk with duplicate detection | M | P2 | Warns on a match of phone, or name + date of birth; creates a `PatientRecord` with a sequential MRN per organization; portal invitation optional |
| FR-FD-03 | Check-in: confirm identity and contact details, show balance, any paid deposit and insurance status, create the queue entry (and, from P4, the encounter) | M | P2 | Only on the appointment date, within a window (default from 60 min before to the end of the day). From P3 the desk can take the visit fee at check-in or at checkout (setting) |
| FR-FD-04 | Queue board per branch, filterable by doctor: waiting for triage, ready for doctor, in consultation, done; wait time per patient; late arrivals flagged | M | P2 | Updates live (push) without a page refresh |
| FR-FD-05 | Call next / assign room / reorder (with a reason) | M | P2 | Calling moves the appointment to `InProgress`; reorders are audited |
| FR-FD-06 | Waiting-room display: ticket numbers and room, no names | S | P2 | A read-only screen per branch, authenticated with a device token |
| FR-FD-07 | Print visit slip and receipt | S | P3 | Thermal (80 mm) and A4 layouts |
| FR-FD-08 | Merge duplicate patients | S | P2 | Moves appointments, encounters, invoices and policies; keeps an audit trail and a pointer from the merged MRN |

### 4.7 Clinical records (EMR)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-EMR-01 | An encounter is created at check-in for each visit | M | P4 | Exactly one encounter per appointment; walk-ins included |
| FR-EMR-02 | Triage: vital signs (BP, pulse, temperature, respiratory rate, SpO₂, weight, height, BMI computed, pain score), chief complaint, allergy confirmation | M | P4 | Values range-checked with warnings for abnormal values; recorded by and at shown |
| FR-EMR-03 | Doctor notes in SOAP structure with specialty templates | M | P4 | Autosave; templates managed per specialty by the medical director |
| FR-EMR-04 | Diagnoses with ICD-10 codes (search by code or term), primary/secondary, status | M | P4 | ICD-10 table seeded; at least one diagnosis required to sign (setting) |
| FR-EMR-05 | Prescriptions: drug (catalog or free text), form, dose, route, frequency, duration, quantity, instructions; printable and in the portal | M | P4 | Allergy conflict warning must be acknowledged; signed prescriptions are immutable; PDF has a verification code |
| FR-EMR-06 | Patient summary lists: allergies, chronic conditions, current medications, past procedures | M | P4 | Shown on every encounter screen; changes tracked with author and date |
| FR-EMR-07 | Orders and referrals: lab, radiology, specialist referral, with a printable form | S | P4 | Order status (ordered, result received); result linked to an attachment |
| FR-EMR-08 | Attachments: PDFs and images (results, reports, photos), by staff or the patient | M | P4 | Max 20 MB per file; virus-scanned; stored outside the database; access-checked download URLs |
| FR-EMR-09 | Sign an encounter, which locks it | M | P4 | After signing, no edits; only addenda; encounters unsigned after 24 h are flagged |
| FR-EMR-10 | Addenda to signed encounters | M | P4 | Shown in order under the original, each with author and time |
| FR-EMR-11 | Void an encounter entered in error, with the medical director's approval | S | P4 | Hidden from normal views; visible in audit with the reason |
| FR-EMR-12 | Patient history timeline: past encounters, vitals trends, prescriptions, attachments, across branches | M | P4 | Respects `encounters.view` scope; break-glass outside it |
| FR-EMR-13 | Patient-facing visit summary: diagnoses (plain names), prescriptions, orders, instructions; no internal notes | S | P4 | Published to the portal when the encounter is signed |
| FR-EMR-14 | Every read of an encounter or attachment is logged | M | P4 | See FR-AUD-02 |
| FR-EMR-15 | V1 `Appointment.DoctorNotes` is migrated into the encounter | M | P4 | See [09 §2](09_MIGRATION_FROM_V1.md#2-importing-v1-data) |

### 4.8 Billing and payments (BIL)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-BIL-01 | An invoice is created for each visit from the services performed, priced by the price list | M | P3 | Created as `Draft` at check-in or completion (setting); issued at checkout by the receptionist; one visit → one invoice (additional invoices allowed). Until EMR (P4) ships, the services come from the booked service plus manual lines |
| FR-BIL-02 | Add manual lines (extra services) and remove lines on drafts | M | P3 | Only active services; free-text lines need `invoices.discount`-level permission |
| FR-BIL-03 | Discounts per line or invoice, percentage or amount, with a reason; limits per role; above-limit needs approval | M | P3 | The approval inbox shows requests; issued invoices show the discount and approver |
| FR-BIL-04 | Payer split: each line has a patient share and a payer share | M | P5 | Computed from the coverage rules (FR-INS-06); shares sum to the line total |
| FR-BIL-05 | Payments at the desk: the **receptionist** sees the fee due (price − paid deposit), collects cash or card (POS), records it in their open cash session and prints the receipt. Also bank transfer, online and wallet; partial payments; several methods per invoice | M | P3 | Works with no `Accountant` in the organization; status moves `Issued` → `PartiallyPaid` → `Paid`; overpayment becomes patient credit |
| FR-BIL-06 | Online payments through **Paymob** (card and mobile wallet): optional booking deposits (FR-APT-08) and open invoices | S | P3 | Paymob hosted checkout, so no card data touches ClinicFlow; a webhook verified with Paymob's HMAC confirms the payment; idempotent on the Paymob transaction id; refunds through Paymob's API |
| FR-BIL-07 | Receipts and invoices as PDF with the organization header and a verification code | M | P3 | Reprint marked "copy" |
| FR-BIL-08 | Refunds: requested with reason, approved per threshold, paid by cash or the original method | M | P3 | Requester ≠ approver; cash refunds go through the open cash session |
| FR-BIL-09 | Cash sessions per user per drawer: open with float, close with count, variance, approval above threshold | M | P3 | One open session per user; cash payments need an open session; closed sessions are immutable |
| FR-BIL-10 | Invoice numbering per branch per year, sequential and gapless: `<BRANCH>-<YYYY>-<000001>` | M | P3 | Concurrency-safe; void keeps the number |
| FR-BIL-11 | Void an issued invoice without payments, with approval | M | P3 | Status `Void`; number kept; audited |
| FR-BIL-12 | Patient balance and statement | S | P3 | Balance = issued patient shares − payments + refunds − credits; statement PDF per period |
| FR-BIL-13 | No-show and late-cancellation fees | C | P3 | Per policy: keep the deposit or post a charge to the balance |
| FR-BIL-14 | A paid deposit is deducted from the visit invoice automatically | S | P3 | The desk sees only the remaining amount; any surplus becomes patient credit or is refunded |
| FR-BIL-15 | Daily close report per branch | M | P3 | Visits, invoices, collections by method, refunds, discounts, unbilled completed visits, cash session variances |
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
| FR-NTF-01 | Channels: in-app (V1), email (V1), SMS. WhatsApp is **C**, added after SMS works | M | P1 (SMS for sign-up), P2 | Providers behind one interface per channel; channel per message type configurable |
| FR-NTF-02 | Templates per event and channel, in Arabic and English, with placeholders | M | P1 | Edited with `notifications.templates`; preview before saving; the recipient's language picks the template (R10) |
| FR-NTF-03 | Events: booking confirmed, reminder, rescheduled, cancelled, no-show, deposit received, invoice issued, payment received, prescription ready, staff invitation, doctor application stage changed (FR-REC-04), phone verification code (FR-ACC-02) | M | P1–P2 | Delivered from the outbox ([06 §5](06_TECHNICAL_DESIGN.md#5-domain-events-and-the-outbox)); verification codes are sent directly, not queued, because they expire in minutes |
| FR-NTF-04 | Patient preferences: the channel for booking confirmations and reminders, **SMS, email or both**; language; opt-out of marketing (no marketing in V2, but the flag exists) | M | P2 | Default: both when the email is verified, SMS otherwise; email can't be chosen without a verified email; transactional messages respect the choice |
| FR-NTF-05 | Delivery log with status per message | S | P2 | Visible to Admin; failed messages retried with backoff |
| FR-NTF-06 | Staff notifications: approvals pending, leave approved, unsigned encounters | S | P2 | In-app |

### 4.11 Reporting (RPT)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-RPT-01 | Branch dashboard: today's bookings vs capacity, queue, waits, no-shows, revenue | M | P2 (revenue P3) | Refreshes at least every minute |
| FR-RPT-02 | Utilisation per doctor and branch | M | P6 | Booked minutes / available minutes, by day/week/month |
| FR-RPT-03 | No-show and cancellation analysis | M | P6 | By doctor, service, weekday, booking source, lead time |
| FR-RPT-04 | Wait-time report and live alerts | S | P2 | Median and p90 check-in → in-progress; alert above threshold |
| FR-RPT-05 | Revenue by branch, doctor, service, payer | M | P3 | Gross, discounts, net; patient vs payer share |
| FR-RPT-06 | Collections and patient receivables aging | M | P3 | Buckets 0–30, 31–60, 61–90, 90+ |
| FR-RPT-07 | Cash session report | M | P3 | Per drawer, per user, variance |
| FR-RPT-08 | Claims report | M | P5 | See FR-INS-12 |
| FR-RPT-09 | Clinical quality: unsigned encounters, encounters signed > 24 h, top diagnoses (aggregate) | S | P4 | No patient identities in aggregate reports |
| FR-RPT-10 | Export every report to CSV/Excel | S | P6 | Exports are audited |
| FR-RPT-11 | **Admin dashboard** for Admin and Manager, with a branch filter: KPI tiles (today's appointments, waiting now, revenue today, open doctor applications) and **at most four charts**: (1) appointments per day for the last 30 days, stacked by outcome (completed, cancelled, no-show); (2) no-show rate per week for the last 12 weeks; (3) revenue collected per day for the last 30 days, split cash, card, Paymob; (4) visits per doctor this month (top 10) | M | P2 (charts 1, 2, 4), P3 (chart 3 and revenue tile) | One endpoint returns ready-made series, not raw rows; loads in < 1 s; anything beyond these four charts is a table or an export (FR-RPT-02…10), not a new chart |

### 4.12 Audit (AUD)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-AUD-01 | Audit entry on every create/update/delete of clinical, financial and access-control records: who, when, where (IP), what (before/after), why (reason if required) | M | P0 | Written in the same transaction as the change |
| FR-AUD-02 | Read logging for encounters, attachments, prescriptions and patient demographics | M | P4 | Includes break-glass flag and reason |
| FR-AUD-03 | Audit viewer with filters (user, patient, entity, date, action) | M | P1 | `audit.view`; export audited too |
| FR-AUD-04 | Audit entries are append-only | M | P0 | No API to edit or delete; DB permissions deny UPDATE/DELETE to the app login |

### 4.13 Patient portal (PAT)

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-PAT-01 | Book by branch, service and doctor | M | P2 | Search filters: branch, specialty, service, doctor, date |
| FR-PAT-02 | See the price before booking | S | P2 | See FR-SVC-04 |
| FR-PAT-03 | Optionally pay a deposit and pay open invoices online (Paymob) | S | P3 | See FR-APT-08, FR-BIL-06 |
| FR-PAT-04 | View and download invoices and receipts | S | P3 | Own only |
| FR-PAT-05 | View visit summaries and prescriptions | S | P4 | See FR-EMR-13 |
| FR-PAT-06 | Upload documents (outside results, insurance card) | S | P4 | See FR-EMR-08, FR-INS-03 |
| FR-PAT-07 | Dependants: a guardian manages a child's bookings and records | C | P6 | Relationship verified at the desk |
| FR-PAT-08 | Consent: terms and data processing consent recorded with version and date | M | P2 | Booking blocked until current consent is accepted ([08 §5](08_SECURITY_PRIVACY_COMPLIANCE.md#5-privacy-and-consent)) |
| FR-PAT-09 | Self-service reschedule and cancel | M | P2 | Obeys the windows in FR-ORG-05 |

### 4.14 Recruitment (REC)

Doctors join the clinic by applying. This replaces V1's self-registration and admin approval (FR-STF-09). Reviewers hold `recruitment.manage` ([02 §5](02_ROLES_AND_PERMISSIONS.md#5-permission-catalog)); the state machine is in [05 §4.6](05_DOMAIN_MODEL.md#46-doctor-application); the journey is [03 J15](03_USER_JOURNEYS.md#j15-doctor-application-to-first-day).

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-REC-01 | A doctor applies: creates an account with **phone and email both verified** (FR-ACC-02), then submits specialty, license number, years of experience, preferred branch, a **CV (required)** and a **cover letter (optional**, a file or text) | M | P1 | CV: PDF or DOCX, ≤ 5 MB, virus-scanned before a reviewer can open it ([06 §8](06_TECHNICAL_DESIGN.md#8-files-and-documents)); one open application per user; status starts at `Applied` |
| FR-REC-02 | Application stages: `Applied` → `Interview` → `Accepted`; `Rejected` from `Applied` or `Interview`; `Withdrawn` by the applicant before acceptance | M | P1 | Any other move returns `DoctorApplication.InvalidStatusTransition` (409); only `recruitment.manage` moves an application, except withdraw |
| FR-REC-03 | Each stage change carries **stage variables** that the reviewer fills in: **Interview**: date and time, branch or address, interviewer name, notes for the applicant; **Accepted**: **start date**, branch, notes; **Rejected**: an optional message | M | P1 | Validated per stage (interview date in the future; start date today or later; branch active); stored with the stage change in the application history |
| FR-REC-04 | An **automatic email at every stage** (`Applied` acknowledgement, `Interview` invitation, `Accepted` with the start date, `Rejected`), plus an SMS when the applicant chose it | M | P1 | AR/EN templates per stage with placeholders, e.g. `{{ApplicantName}}`, `{{InterviewDateTime}}`, `{{InterviewLocation}}`, `{{InterviewerName}}`, `{{StartDate}}`, `{{Branch}}`, `{{Notes}}`, filled from FR-REC-03; the reviewer sees the rendered message before confirming; sent through the outbox, so a failed send never undoes the stage change; the delivery shows in the history |
| FR-REC-05 | Accepting creates the staff member: role `Doctor` at the chosen branch, a doctor profile from the application, status `Inactive` until the start date, then activated by a daily job | M | P1 | The profile stays hidden (`IsPublic = false`) until a Manager publishes it; no slots exist until a schedule is created; the applicant's account is reused, not duplicated |
| FR-REC-06 | Applicants see their own application: current stage, history, interview details, start date; they can withdraw | M | P1 | Own application only; a withdrawn or rejected applicant can apply again after a cool-down (setting, default 90 days) |
| FR-REC-07 | Reviewers get an application list (filter by status, specialty, branch, date; paged), the detail with access-checked CV and cover-letter download, and the full history | M | P1 | `recruitment.view` in the application's preferred branch, or organization scope; every download writes an audit entry |
| FR-REC-08 | Files of rejected and withdrawn applications are deleted after a retention period (setting, default 6 months) | S | P1 | A daily job deletes the files and keeps the application row with a "files deleted" marker; the period is confirmed with counsel ([08 §6](08_SECURITY_PRIVACY_COMPLIANCE.md#6-retention-and-deletion)) |

### 4.15 Accounts and sign-in (ACC)

Most patients identify by mobile number, and many have no email. Security controls for codes are in [08 §2](08_SECURITY_PRIVACY_COMPLIANCE.md#2-threats-and-controls).

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-ACC-01 | Patient sign-up with an **Egyptian mobile number** and a password; **email optional** | M | P1 | Number validated as an Egyptian mobile (`01[0125]` + 8 digits, or `+20…`) and stored as E.164; unique among accounts; the password policy is V1's |
| FR-ACC-02 | **SMS one-time code** verifies the phone at sign-up and whenever it changes | M | P1 | 6 digits; valid 5 minutes; at most 5 attempts per code; resend after 60 s; daily caps per number and per IP; codes stored hashed; booking and applying require a verified phone; the account is usable only after verification |
| FR-ACC-03 | Log in with phone **or** email, plus the password | M | P1 | One `identifier` field; errors never reveal whether an account exists; lockout as V1 |
| FR-ACC-04 | Reset the password by SMS code or by email link | M | P1 | Same code rules as FR-ACC-02; a reset revokes all refresh tokens (V1 behaviour) |
| FR-ACC-05 | Change the phone or add an email later | M | P1 | A new phone needs a code before it replaces the old one; a new email gets a confirmation link (V1 flow) and can be used for login and notifications only once confirmed |
| FR-ACC-06 | Each user has a **preferred language**, `ar` (default) or `en` | M | P1 | Set at sign-up from the UI language, changeable in the profile; drives every email and SMS (R10) |

### 4.16 Chatbot (BOT)

Built last. The scope is confirmed at the P7 kickoff (open question Q16 in [10 §5](10_ROADMAP.md#5-open-questions)).

| Id | Requirement | Pri | Phase | Acceptance criteria |
|---|---|---|---|---|
| FR-BOT-01 | A bilingual (AR/EN) assistant in the patient portal answers clinic questions (branches, hours, services, prices, how to prepare for a visit) and helps the patient find and book a slot | C | P7 | Answers only from clinic data and approved FAQ text; gives **no medical advice** and says so; booking goes through the normal booking endpoint, so every V2 rule applies; no patient health data is sent to the model provider without legal approval; conversations are logged without clinical content |

## 5. Non-functional requirements

| Id | Requirement | Target |
|---|---|---|
| NFR-01 | Availability during clinic hours | 99.5% monthly |
| NFR-02 | API latency | p95 < 300 ms for reads, < 800 ms for writes, excluding external providers |
| NFR-03 | Capacity | 10 branches, 150 staff, 200k patients, 2,000 appointments/day without redesign |
| NFR-04 | Backup and restore | Full daily, log every 15 min; RPO ≤ 15 min, RTO ≤ 4 h; restore tested quarterly |
| NFR-05 | Security | OWASP ASVS level 2 for the API; MFA for staff; TLS 1.2+ everywhere ([08](08_SECURITY_PRIVACY_COMPLIANCE.md)) |
| NFR-06 | Privacy | Compliance checklist in [08 §5](08_SECURITY_PRIVACY_COMPLIANCE.md#5-privacy-and-consent) signed off before go-live |
| NFR-07 | Localisation | Arabic (RTL, default) and English UI; codes not texts from the API; dates in branch-local time; every email and SMS in the recipient's preferred language (V1's English-only email templates are rewritten as AR/EN templates); bilingual reference data (R9) |
| NFR-08 | Accessibility | WCAG 2.1 AA for the patient portal |
| NFR-09 | Observability | Structured logs (Serilog, as V1), health endpoint, metrics for queue length, outbox lag, job failures |
| NFR-10 | Data retention | Clinical and financial records kept per the policy in [08 §6](08_SECURITY_PRIVACY_COMPLIANCE.md#6-retention-and-deletion) |
| NFR-11 | Test coverage | Every state transition, permission rule and money calculation has automated tests |
| NFR-12 | Browser support | Last two versions of Chrome, Edge, Safari, Firefox |
