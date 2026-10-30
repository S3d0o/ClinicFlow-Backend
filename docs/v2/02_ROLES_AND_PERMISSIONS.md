# 02: Roles and Permissions

## 1. Overview

V1 has three roles checked as strings in controllers and services (`Admin`, `Doctor`, `Patient`). V2 has seven roles. A role is only a **bundle of permissions**, and every permission is granted with a **scope** (own records, a branch, or the whole organization). Code checks permissions, never role names ([ADR 0002](adr/0002-permission-based-authorization.md)).

```
Organization (one in V2)
├── Admin            system administration: accounts, roles, settings, integrations, audit
├── Manager          organization-wide (owner, medical director) or branch-scoped (branch manager)
└── Branch A, Branch B, …
     ├── Receptionist   front desk: patients, bookings, check-in, queue, payments at the desk
     ├── Nurse          triage, vitals, room preparation, assisting the doctor
     ├── Doctor         schedules, encounters, prescriptions, orders
     └── Accountant     optional: invoices, refunds, cash sessions, insurance claims (often organization-wide)
Patient              outside the staff tree: their own bookings, records, invoices
Doctor applicant     outside the staff tree, no role: their own application only
```

**Clinics without an accountant.** Most Egyptian clinics have none: the receptionist takes the money and confirms the visit, and the manager approves exceptions. The default matrix (§6) lets `Receptionist` + `Manager` run billing end to end. `Accountant` is optional, for clinics that have one.

## 2. Roles

| Role | Typical person | Scope | Main job in the system |
|---|---|---|---|
| `Admin` | IT officer, system owner | Organization | Staff accounts and roles, organization settings, integrations, audit log, security. **Does not** see clinical content by default |
| `Manager` | Owner, medical director (organization); branch manager (branch) | Organization or Branch | Branches, rooms, services and prices, doctor schedules and leave, **doctor applications**, approvals (refunds, discounts, voids, cash variances), daily close, reports |
| `Receptionist` | Front-desk staff, call-centre agent | Branch | Register patients, book / reschedule / cancel, walk-ins, check-in, queue, **collect payments and issue invoices at checkout**, own cash session, insurance card capture |
| `Nurse` | Nurse, medical assistant | Branch | Queue view, triage and vitals, prepare the patient for the doctor, upload documents |
| `Doctor` | Physician, dentist, specialist | Own (+ Branch for reads) | Own schedule and appointments, encounters, diagnoses, prescriptions, orders, signing |
| `Accountant` (optional) | Cashier supervisor, accountant, insurance officer | Branch or Organization | Invoices, payments, refunds, cash sessions, payers and plans, claims, receivables, financial reports |
| `Patient` | Patient, or guardian for a minor (V2.x) | Own | Book online, optionally pay a deposit, manage own appointments, view own invoices, prescriptions and visit summaries, manage insurance policy |

A **doctor applicant** is a user with a doctor application and **no role**. They can only read and withdraw their own application ([04 §4.14](04_FUNCTIONAL_REQUIREMENTS.md#414-recruitment-rec)). Acceptance turns them into a `Doctor` staff member.

### 2.1 Personas

**Admin: Karim, IT officer.**
- **Goals:** nobody has more access than their job needs; leavers lose access the same day; the system is up.
- **Daily tasks:** invite staff, assign roles and branches, reset MFA, review the audit log, configure messaging and payment settings.
- **Pain today (V1):** the V1 Admin also approves doctors and edits specialties; business and technical duties are mixed.

**Manager (organization): Dr. Hala, medical director and co-owner.**
- **Goals:** quality of care, revenue, growth to a third branch.
- **Daily tasks:** review the dashboard (revenue, no-shows, wait time, unsigned encounters), approve large refunds and discounts, set prices.
- **Pain:** has no numbers until the accountant sends a spreadsheet at month end.

**Manager (branch): Omar, branch manager.**
- **Goals:** a smooth day at his branch; no idle doctors, no overflowing waiting room.
- **Daily tasks:** publish doctor schedules, handle doctor leave and reschedules, manage rooms, review doctor applications and schedule interviews, approve discounts, refunds and cash variances, handle complaints, run the daily close.
- **Pain:** finds out a doctor is on leave when patients are already waiting.

**Receptionist: Mona, front desk.**
- **Goals:** register and check in a patient in under a minute, keep the queue moving, never double-book.
- **Daily tasks:** answer calls and book, register walk-ins, check patients in, collect the visit fee (minus any online deposit) in cash or by card, print receipts, close her cash session, capture insurance cards, reschedule when a doctor is late.
- **Pain:** online bookings appear in a different place than phone bookings; she can't see who is waiting for which doctor.

**Nurse: Sara.**
- **Goals:** fast, accurate triage; the doctor sees vitals before the patient walks in.
- **Daily tasks:** take the next patient from the queue, record vitals, chief complaint and allergies, upload outside reports, prepare the room.
- **Pain:** vitals are written on paper and often lost.

**Doctor: Dr. Ahmed, cardiologist, works at two branches.**
- **Goals:** see the whole history in one screen, document quickly, prescribe without errors.
- **Daily tasks:** look at today's list, call the next patient, write notes, diagnoses and prescriptions, order tests, sign encounters, set leave.
- **Pain:** V1 gives him one free-text notes field and no history.

**Accountant (optional): Youssef.** Only in clinics that employ one; otherwise Mona and Omar cover his billing tasks.
- **Goals:** every visit is billed, every drawer reconciles, insurers pay on time.
- **Daily tasks:** review the daily close per branch, follow up unpaid invoices, prepare and submit monthly claims, record payer remittances, handle rejected claims.
- **Pain:** reconciles cash against paper receipts; rebuilds claims from scratch every month.

**Patient: Mohamed.**
- **Goals:** book quickly, know the price, wait less, get his prescription and receipt.
- **Daily tasks:** book online, receive reminders, check in at the desk, pay, download the prescription.
- **Pain:** doesn't know the price or whether his insurance covers the visit.

## 3. Staff assignments

- A **staff member** belongs to the organization and has one or more **roles**.
- Each role is held **with a scope**: `Organization`, or a list of branches. Examples:
  - Dr. Hala: `Manager` @ Organization, `Doctor` @ Branch A.
  - Dr. Ahmed: `Doctor` @ Branch A and Branch B.
  - Youssef: `Accountant` @ Organization.
  - Mona: `Receptionist` @ Branch A.
- **Doctors are staff.** A doctor joins in one of two ways:
  - **Application** (the usual way): the doctor applies online with a CV; a reviewer with `recruitment.manage` moves the application to `Interview` and then `Accepted`, which creates the staff member ([04 FR-REC](04_FUNCTIONAL_REQUIREMENTS.md#414-recruitment-rec)). This replaces V1's self-registration and admin approval.
  - **Invitation** by a Manager or Admin, for doctors hired outside the pipeline, as for every other staff role ([04 FR-STF](04_FUNCTIONAL_REQUIREMENTS.md#42-staff-and-access-stf)).
- A **patient account** can also belong to a staff member (a nurse who is also a patient). The two capacities are separate: staff permissions never apply to their own patient record beyond what any patient sees.
- Deactivating a staff member revokes their sessions immediately (refresh tokens) and their access tokens expire within 15 minutes, as in V1 H3.

*V3 note:* a staff member is a `(UserId, OrganizationId)` pair, so one person can work for two organizations with different roles.

## 4. Permission model

- A **permission** is a string `<resource>.<action>`, e.g. `appointments.check_in`.
- A role grants a permission at a **scope**:

| Scope | Means |
|---|---|
| `Own` | Only records the user owns: a doctor's own appointments and encounters; a patient's own data |
| `Branch` | Records of the branches the user is assigned to |
| `Organization` | All records of the organization |

- **Evaluation:** an endpoint requires a permission (policy). The service then checks the **resource**: does the record's branch (or owner) fall inside the user's scope for that permission? This repeats V1's ownership check pattern (`AppointmentService.GetAppointmentAsync`), generalised.
- **Effective permission** is the union across all the user's roles. The widest scope wins.
- **Deny by default.** No permission → 403. The authorization fallback policy requires an authenticated user everywhere unless an endpoint is explicitly public (fixes V1 NM5).
- **Clinical content** (encounters, notes, diagnoses, prescriptions, attachments) has its own permissions. `Admin` and `Accountant` do not get them. Billing needs only service codes and diagnosis codes on claims, which come through `claims.*`.
- **Break-glass:** a user with `records.break_glass` can open a record outside their scope after entering a reason. It is time-limited (default 60 min), audited, and reported to the medical director ([08 §4](08_SECURITY_PRIVACY_COMPLIANCE.md#4-access-control-and-audit)).

## 5. Permission catalog

**Module** is the code of the module the permission belongs to ([04 §3](04_FUNCTIONAL_REQUIREMENTS.md#3-module-index)). It is authoritative ([ADR 0008](adr/0008-multi-clinic-platform-module-entitlements.md) decision 3): a grant counts only while that module is enabled for the organization, and `PermissionMatrixTests` compares this column with `Shared/Authorization/Modules.cs`. **Area** only groups the rows: the queue and triage (`queue.*`, `vitals.write`) are the front desk add-on (FD), while booking on behalf and check-in stay in core APT, so a clinic without the front desk still runs a calendar. The operational dashboard (`reports.operational`) is core APT and `reports.financial` comes with billing (BIL).

| Area | Permission | Module | Description |
|---|---|---|---|
| Organization | `org.settings.manage` | ORG | Organization profile, branding, global settings |
| | `branches.manage` | ORG | Create/edit branches, working hours, holidays |
| | `rooms.manage` | ORG | Create/edit rooms |
| Staff | `staff.view` | STF | See staff list and assignments |
| | `staff.invite` | STF | Invite staff, assign roles and branches |
| | `staff.deactivate` | STF | Deactivate staff, revoke sessions |
| | `roles.manage` | STF | Edit which permissions a role grants (Admin only; changes are audited) |
| Recruitment | `recruitment.view` | REC | See doctor applications and download CVs and cover letters |
| | `recruitment.manage` | REC | Move applications between stages (interview, accept, reject) and send the stage emails |
| Catalog | `specialties.manage` | SVC | Specialty catalog (V1 feature) |
| | `services.manage` | SVC | Service catalog |
| | `prices.manage` | SVC | Price lists |
| Scheduling | `schedules.view` | SCH | See doctor schedules |
| | `schedules.manage` | SCH | Create/edit/deactivate schedules for doctors |
| | `schedules.manage_own` | SCH | A doctor editing their own schedule (an organization setting decides whether doctors may edit directly or only request changes) |
| | `leave.request` | SCH | Request leave for oneself |
| | `leave.approve` | SCH | Approve leave and trigger rescheduling |
| | `slots.block` | SCH | Block or unblock individual slots |
| | `slots.overbook` | SCH | Book beyond capacity (double-book a slot) |
| Patients | `patients.view` | APT | Search and view patient demographics |
| | `patients.register` | APT | Create patient records at the desk |
| | `patients.edit` | APT | Edit demographics, contacts, consent flags |
| | `patients.merge` | APT | Merge duplicate patient records |
| Appointments | `appointments.view` | APT | See appointments and the calendar |
| | `appointments.book_on_behalf` | APT | Book for a patient (phone, desk) |
| | `appointments.reschedule` | APT | Move an appointment |
| | `appointments.cancel` | APT | Cancel (any time; patient self-cancel keeps the 2-hour rule) |
| | `appointments.check_in` | APT | Check in, register walk-ins |
| | `appointments.mark_no_show` | APT | Mark no-show |
| | `queue.view` | FD | See the branch queue |
| | `queue.manage` | FD | Reorder the queue, call patients, assign rooms |
| Clinical | `encounters.view` | EMR | Read encounters and history |
| | `encounters.write` | EMR | Write notes, diagnoses, orders |
| | `encounters.sign` | EMR | Sign (lock) an encounter |
| | `encounters.addend` | EMR | Add an addendum to a signed encounter |
| | `encounters.void` | EMR | Void an encounter entered in error (needs approval) |
| | `vitals.write` | FD | Record triage and vital signs |
| | `prescriptions.write` | EMR | Create and sign prescriptions |
| | `attachments.upload` | EMR | Upload clinical documents and results |
| | `records.break_glass` | EMR | Emergency access outside normal scope |
| Billing | `invoices.view` | BIL | See invoices |
| | `invoices.create` | BIL | Create and issue invoices, add lines |
| | `invoices.discount` | BIL | Apply a discount up to the role's limit |
| | `invoices.void` | BIL | Void an issued invoice (needs approval) |
| | `payments.collect` | BIL | Record cash/card/online payments |
| | `refunds.request` | BIL | Request a refund |
| | `refunds.approve` | BIL | Approve refunds (threshold rules in §7) |
| | `cash_sessions.operate` | BIL | Open/close one's own cash session |
| | `cash_sessions.reconcile` | BIL | Review and approve other users' session closes |
| Insurance | `payers.manage` | INS | Payers, plans, coverage rules |
| | `policies.manage` | INS | Patient insurance policies, eligibility checks |
| | `preauth.manage` | INS | Request and record pre-authorizations |
| | `claims.manage` | INS | Build, submit and track claims; record remittances |
| Reporting | `reports.operational` | APT | Utilisation, queue, no-shows |
| | `reports.clinical` | RPT | Diagnoses, record completeness (aggregate, no identities) |
| | `reports.financial` | BIL | Revenue, collections, claims, cash |
| System | `audit.view` | AUD | Read the audit log |
| | `integrations.manage` | NTF | Messaging (WhatsApp, SMS, email) and payment gateway settings |
| | `notifications.templates` | NTF | Edit WhatsApp/SMS/email templates |

## 6. Role × permission matrix

Legend: **O** = Organization, **B** = assigned Branch(es), **S** = own records only, **—** = not granted. A `Manager` holds Branch or Organization scope depending on their assignment; the table shows `B/O`.

| Permission | Admin | Manager | Receptionist | Nurse | Doctor | Accountant | Patient |
|---|---|---|---|---|---|---|---|
| `org.settings.manage` | O | O (org managers only) | — | — | — | — | — |
| `branches.manage` | O | B/O | — | — | — | — | — |
| `rooms.manage` | — | B/O | — | — | — | — | — |
| `staff.view` | O | B/O | B | B | B | B/O | — |
| `staff.invite` | O | B/O (not `Admin` / `Manager` roles) | — | — | — | — | — |
| `staff.deactivate` | O | B/O | — | — | — | — | — |
| `roles.manage` | O | — | — | — | — | — | — |
| `recruitment.view` | — (grantable) | B/O | — | — | — | — | — |
| `recruitment.manage` | — (grantable) | B/O | — | — | — | — | — |
| `specialties.manage` | O | O | — | — | — | — | — |
| `services.manage` | — | B/O | — | — | — | — | — |
| `prices.manage` | — | B/O | — | — | — | O | — |
| `schedules.view` | — | B/O | B | B | B | — | public slots only |
| `schedules.manage` | — | B/O | — | — | — | — | — |
| `schedules.manage_own` | — | — | — | — | S | — | — |
| `leave.request` | — | — | — | — | S | — | — |
| `leave.approve` | — | B/O | — | — | — | — | — |
| `slots.block` | — | B/O | B | — | S | — | — |
| `slots.overbook` | — | B/O | — | — | S | — | — |
| `patients.view` | — | B/O | B | B | B | B/O | S |
| `patients.register` | — | B/O | B | — | — | — | S (self sign-up) |
| `patients.edit` | — | B/O | B | — | — | — | S |
| `patients.merge` | — | O | — | — | — | — | — |
| `appointments.view` | — | B/O | B | B | S (+B read) | B/O | S |
| `appointments.book_on_behalf` | — | B/O | B | — | — | — | — |
| `appointments.reschedule` | — | B/O | B | — | S | — | S (rules apply) |
| `appointments.cancel` | — | B/O | B | — | S | — | S (≥ 2 h before) |
| `appointments.check_in` | — | B/O | B | B | — | — | — |
| `appointments.mark_no_show` | — | B/O | B | — | S | — | — |
| `queue.view` | — | B/O | B | B | B | — | own position |
| `queue.manage` | — | B/O | B | B | S | — | — |
| `encounters.view` | — | O (medical director only) | — | B | B | — | S (summary) |
| `encounters.write` | — | — | — | — | S | — | — |
| `encounters.sign` | — | — | — | — | S | — | — |
| `encounters.addend` | — | — | — | — | S | — | — |
| `encounters.void` | — | O (approver) | — | — | S (request) | — | — |
| `vitals.write` | — | — | — | B | B | — | — |
| `prescriptions.write` | — | — | — | — | S | — | — |
| `attachments.upload` | — | — | B (non-clinical: IDs, insurance cards) | B | B | — | S (own documents) |
| `records.break_glass` | — | O (medical director) | — | — | B | — | — |
| `invoices.view` | — | B/O | B | — | S (own visits, amounts only) | B/O | S |
| `invoices.create` | — | B/O | B (issue at checkout) | — | — | B/O | — |
| `invoices.discount` | — | B/O (any amount) | B (≤ limit) | — | — | B/O (≤ limit) | — |
| `invoices.void` | — | B/O (approver) | — | — | — | B/O (request) | — |
| `payments.collect` | — | B/O | B (cash, card) | — | — | B/O | S (online deposit, Paymob) |
| `refunds.request` | — | B/O | B | — | — | B/O | — |
| `refunds.approve` | — | B/O | — | — | — | O (≤ limit) | — |
| `cash_sessions.operate` | — | — | B | — | — | B | — |
| `cash_sessions.reconcile` | — | B/O | — | — | — | B/O | — |
| `payers.manage` | — | O | — | — | — | O | — |
| `policies.manage` | — | B/O | B | — | — | B/O | S (upload card) |
| `preauth.manage` | — | B/O | B | — | S (request) | B/O | — |
| `claims.manage` | — | O | — | — | — | B/O | — |
| `reports.operational` | — | B/O | — | — | S | B/O | — |
| `reports.clinical` | — | O (medical director) | — | — | S | — | — |
| `reports.financial` | — | B/O | — | — | S (own revenue) | B/O | — |
| `audit.view` | O | O (org managers only) | — | — | — | — | — |
| `integrations.manage` | O | — | — | — | — | — | — |
| `notifications.templates` | O | O | — | — | — | — | — |

Notes:
- "Medical director" is a `Manager` @ Organization who is also a licensed doctor. It is a **flag on the staff member** (`IsMedicalDirector`), not a separate role, because it only changes clinical access.
- A doctor's `B` read access to encounters means doctors of the same branch can read each other's encounters **for patients they are treating**. This is controlled by the setting `clinical.shareWithinBranch` (default on); when off, doctors see only encounters they wrote, plus break-glass.
- The matrix is the **default**. `roles.manage` lets the Admin adjust it per organization; every change is audited, and clinical permissions can never be granted to `Admin` or `Accountant` (a hard rule in code).
- **Recruitment** is a permission, not a role: `Manager` has it by default, and the Admin can grant `recruitment.*` to any role (e.g. to a staff member who does HR work). There is no `HR` role.
- **Without an accountant**, every billing task has a default owner: `Receptionist` collects, issues invoices at checkout and operates their own cash session; `Manager` approves refunds, discounts, voids and cash variances and runs the daily close. The `Accountant` column only matters when the clinic has one.

## 7. Approvals (four-eyes rules)

| Action | Who requests | Who approves | Rule (configurable per organization) |
|---|---|---|---|
| Discount above the requester's limit | Receptionist, Accountant | Manager | Receptionist default limit 10%, Accountant 20% |
| Refund | Receptionist, Accountant | Accountant (≤ 1,000 EGP) or Manager (any) | Refunds over the threshold need a Manager; without an accountant, the Manager approves every refund |
| Invoice void | Accountant | Manager | Only invoices with no payments; otherwise refund first |
| Encounter void | Doctor (author) | Medical director | The encounter stays in the record, marked `Voided` with the reason |
| Doctor leave | Doctor | Manager of the branch | Approval triggers the bulk reschedule flow ([03 J5](03_USER_JOURNEYS.md#j5-doctor-leave-and-bulk-reschedule)) |
| Patient merge | Receptionist (flag) | Manager @ Organization | Irreversible except by Admin restore from audit |
| Cash session variance above threshold | Cashier (close) | Accountant or Manager | Default threshold 50 EGP |

Requester and approver must be different people. Every approval is audited with both identities.

## 8. What each role sees first

| Role | Home screen |
|---|---|
| Admin | Staff list, pending invitations, integration health, recent security events |
| Manager | Dashboard (FR-RPT-11): KPI tiles and at most four charts; today's queue and wait time, unsigned encounters, pending approvals, open doctor applications |
| Receptionist | Today's calendar for the branch, the queue, search-or-register patient, open cash session |
| Nurse | Queue filtered to "waiting for triage", room status |
| Doctor | Today's list across branches, next patient with triage summary, unsigned encounters |
| Accountant | Unbilled visits, open cash sessions, claims by status, outstanding patient balances |
| Patient | Upcoming appointments, book again, recent prescriptions and invoices |
| Doctor applicant | Their application: current stage, history, interview details, start date |

## 9. Changes from V1

| V1 | V2 |
|---|---|
| Roles `Admin`, `Doctor`, `Patient` checked as strings | Seven roles; code checks permissions; role names are constants |
| `Admin` approves doctors and manages specialties | `Admin` is technical. Managers review doctor applications (or invite doctors); specialties are `specialties.manage` (Admin or org Manager) |
| Doctors self-register and wait for approval (M1 gap: unapproved doctors can log in) | Doctors **apply** with a CV and go through `Applied` → `Interview` → `Accepted`. An applicant has no role, so the M1 gap disappears: only acceptance creates a `Doctor` staff member |
| Patients register with an email | Patients register with a mobile number verified by a one-time code (WhatsApp, or SMS when on); email is optional |
| Doctor cancels any own appointment any time | Same, plus reschedule and leave flows |
| Patients cancel ≥ 2 h before | Unchanged; the window becomes an organization setting |
| One role per user (`UserProfileResponse.role`) | Several roles; the profile returns `roles[]` and `branches[]` ([07](07_API_DESIGN.md)) |
