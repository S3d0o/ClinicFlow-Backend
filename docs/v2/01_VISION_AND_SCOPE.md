# 01: Vision and Scope

## 1. Problem

A clinic runs on more than bookings. On a normal day at a busy outpatient clinic:
- **Phone and walk-in patients** are written on paper or in a spreadsheet, so online and offline bookings collide.
- **The waiting room is invisible.** Doctors don't know who has arrived, and patients don't know how long they will wait.
- **Nurses take vitals on paper**, and the doctor re-asks the same questions.
- **Clinical notes live in a free-text field** (`Appointment.DoctorNotes` in V1). There are no diagnoses, prescriptions, history or attachments.
- **Money is handled outside the system.** Nobody can say at 9 pm how much cash should be in the drawer, which visits were never billed, or what the insurers owe.
- **Insurance patients** need eligibility checks, pre-approvals and monthly claims, all done by hand.
- **The manager has no numbers:** utilisation, no-show rate, revenue per doctor, outstanding claims.

V1 solves only the first minute of that day: a patient choosing a slot online.

## 2. Vision

> **ClinicFlow V2 runs the whole outpatient visit for a multi-branch clinic: book, arrive, triage, treat, bill, claim, and report, with each staff member seeing exactly what their job needs.**

## 3. Goals

| # | Goal | Measured by |
|---|---|---|
| G1 | One calendar for online, phone and walk-in bookings | 0 double bookings; ≥ 95% of visits exist in the system |
| G2 | A visible, fair waiting room | Median check-in → doctor wait is measured and shown per branch |
| G3 | Structured clinical records | ≥ 90% of completed visits have a signed encounter within 24 h |
| G4 | Every visit is billed and every pound is accounted for | 100% of completed visits have an invoice; daily cash sessions close with a recorded variance |
| G5 | Insurance visits are paid | Claim rejection rate and days-to-payment are tracked; rejected claims are resubmitted from the system |
| G6 | Fewer no-shows | No-show rate tracked per branch and doctor; reminders by SMS/WhatsApp; optional deposits and no-show fees |
| G7 | Managers decide from data | Daily, weekly and monthly reports per branch, doctor and service |
| G8 | Ready for a multi-clinic platform | No V2 table or API needs a breaking change to support more than one organization ([ADR 0001](adr/0001-single-organization-tenant-ready.md)) |

### Success metrics (baseline in the first month after go-live, then targets)

| Metric | Definition | Target after 3 months |
|---|---|---|
| No-show rate | `NoShow` / (`Completed` + `NoShow`) | −30% vs baseline |
| Wait time | Median minutes from `CheckedIn` to `InProgress` | Published per branch; −20% vs baseline |
| Billing closure | Completed visits with an issued invoice on the same day | ≥ 98% |
| Cash variance | Absolute variance across closed cash sessions / cash taken | < 0.5% |
| Claim rejection | Rejected claim lines / submitted claim lines | Tracked; −25% vs baseline |
| Record completeness | Completed visits with a signed encounter within 24 h | ≥ 90% |
| Slot utilisation | Booked minutes / available minutes per doctor | Reported weekly |

## 4. Scope

### 4.1 In scope for V2

| Module | Summary |
|---|---|
| **Organization and branches** | One organization, many branches: addresses, time zones, working hours, holidays, rooms, settings |
| **Staff and access** | Staff accounts by invitation, roles, branch assignments, permission-based access, deactivation, MFA |
| **Services and pricing** | Service catalog (consultation, follow-up, procedures) with duration and price per branch or doctor |
| **Scheduling 2.0** | Doctor schedules per branch and room, exceptions, leave, holidays, a rolling slot window, blocking, controlled overbooking |
| **Front desk** | Booking on behalf of patients, walk-ins, check-in, queue board, reschedule, no-show handling, patient registration |
| **Clinical records (EMR)** | Encounters, triage vitals, SOAP notes, ICD-10 diagnoses, prescriptions, referrals and orders, attachments, signing, addenda, patient history |
| **Billing and payments** | Invoices from visits, discounts, deposits, cash/card/online payments, receipts, refunds, cash sessions, branch numbering |
| **Insurance** | Payers, plans, coverage rules, patient policies, eligibility, co-pay split, pre-authorizations, claims, denials and resubmission |
| **Notifications** | In-app (existing), email (existing), SMS/WhatsApp templates for reminders, confirmations and changes |
| **Reporting** | Operational, clinical and financial reports and dashboards per role |
| **Audit** | Who did what, when, from where, on every sensitive record |
| **Patient portal updates** | Book by branch and service, see prices, pay deposits, view invoices, prescriptions and visit summaries, upload insurance cards |

### 4.2 Out of scope for V2 (candidates for later versions)

| Item | Why not now |
|---|---|
| Multiple organizations, platform admin, cross-clinic marketplace | V3. The schema is prepared for it ([ADR 0001](adr/0001-single-organization-tenant-ready.md)) |
| Inventory, consumables, equipment maintenance | Explicitly excluded by the business |
| Lab and radiology information systems | V2 creates **orders/referrals** and stores **result attachments**; it does not run a lab |
| Pharmacy dispensing | V2 writes prescriptions; dispensing is external |
| HR and payroll (attendance, salaries, doctor commission payouts) | V2 reports revenue per doctor; payouts are calculated outside |
| Telemedicine (video visits) | Separate product decision |
| Direct electronic integration with insurers / TPAs | V2 produces claim exports and tracks status manually; integrations depend on each payer ([10 §5](10_ROADMAP.md#5-open-questions)) |
| In-patient, beds, surgery theatre management | Outpatient clinics only |
| Native mobile apps | The React web app stays responsive; native apps are a separate project |

## 5. V2 versus V3

| Concern | V2 (this plan) | V3 (later) |
|---|---|---|
| Organizations | Exactly one, created at install | Many; self-service clinic onboarding |
| Top role | `Admin` = system administrator of the one clinic | Adds a platform operator above all organizations |
| Data partition | Every clinic-owned row carries `OrganizationId`; one value in practice | Same columns, many values; global query filters already in place |
| Patients | Global user account + an organization-scoped `PatientRecord` (MRN) | Same: one patient account, a record per clinic visited |
| Public search | Doctors and services of the one organization, filtered by branch | Cross-clinic marketplace |
| Billing | The clinic bills its patients | Adds platform subscription billing of clinics |
| Configuration | Organization settings table | Same table, per organization |

The rule for V2 work: **if a design would have to change when a second organization appears, change the design now.** Each document marks these points with a *V3 note*.

## 6. Assumptions

- The clinic operates in **Egypt**. Currency is **EGP**. Amounts are stored as `decimal(18,2)`.
- The user interface is **Arabic and English**. The backend stores user-entered text as-is and returns error codes, so the frontend translates.
- Each branch has a **time zone** (default `Africa/Cairo`). All slot dates and times are branch-local wall-clock times ([ADR 0003](adr/0003-branch-time-zones.md)).
- Patients may visit **any branch**. Their record and history are shared across the organization's branches.
- Staff use desktops at the front desk and in consultation rooms, and tablets or phones for the nurse station.
- Internet is available at every branch. Offline operation is not a V2 requirement (see [10 §4](10_ROADMAP.md#4-risks)).
- One doctor can work at several branches on different days, but in only one place at a time.

## 7. Constraints

- **Keep the V1 architecture** (Clean Architecture, Result pattern, `IUnitOfWork`, ASP.NET Identity, SQL Server, React frontend). V2 extends it; see [06](06_TECHNICAL_DESIGN.md).
- **The API is shared with a separate frontend repo.** V2 is contract version 2.0.0 and follows the change process in [API_CONTRACT.md §11](../API_CONTRACT.md#11-how-to-change-this-contract).
- **Medical and payment data are regulated.** No card numbers are stored; health data is sensitive personal data under Egyptian law ([08](08_SECURITY_PRIVACY_COMPLIANCE.md)).
- **Open V1 defects block V2 work** where they touch the same code (slots, time zone, error shapes). They are fixed in phase P0 ([10](10_ROADMAP.md)).

## 8. Stakeholders

| Stakeholder | Interest |
|---|---|
| Clinic owner / medical director | Revenue, quality of care, compliance, growth to more branches |
| Branch managers | Smooth days, staff coverage, utilisation, complaints |
| Doctors | Less admin, complete patient history, fair schedules |
| Front desk | Fast booking, clear queue, fewer angry patients |
| Nurses | Quick triage entry, clear handover to the doctor |
| Accountants | Every visit billed, cash reconciled, claims paid |
| Patients | Easy booking, short waits, transparent prices, access to their prescriptions and invoices |
| IT / system admin | Secure, observable, maintainable system |
| Insurers / TPAs (external) | Correct, complete claims with approvals attached |
