# 03: User Journeys

Each journey describes one real situation at the clinic, end to end. The **System** column is what ClinicFlow does. Requirement ids point to [04_FUNCTIONAL_REQUIREMENTS.md](04_FUNCTIONAL_REQUIREMENTS.md); status values come from [05 §4](05_DOMAIN_MODEL.md#4-state-machines).

| # | Journey | Main roles |
|---|---|---|
| J1 | [A complete visit: online booking to follow-up](#j1-a-complete-visit-online-booking-to-follow-up) | Patient, Receptionist, Nurse, Doctor |
| J2 | [Phone booking](#j2-phone-booking) | Receptionist |
| J3 | [Walk-in patient](#j3-walk-in-patient) | Receptionist, Nurse, Doctor |
| J4 | [Reschedule](#j4-reschedule) | Patient, Receptionist |
| J5 | [Doctor leave and bulk reschedule](#j5-doctor-leave-and-bulk-reschedule) | Doctor, Manager, Receptionist |
| J6 | [Late arrival and no-show](#j6-late-arrival-and-no-show) | Receptionist, Doctor |
| J7 | [Insurance visit](#j7-insurance-visit) | Receptionist, Doctor, Accountant |
| J8 | [Refund](#j8-refund) | Receptionist, Accountant, Manager |
| J9 | [End-of-day cash close](#j9-end-of-day-cash-close) | Receptionist, Manager (or Accountant) |
| J10 | [Monthly claims cycle](#j10-monthly-claims-cycle) | Accountant |
| J11 | [Patient views records and invoices](#j11-patient-views-records-and-invoices) | Patient |
| J12 | [Staff onboarding and offboarding](#j12-staff-onboarding-and-offboarding) | Admin, Manager |
| J13 | [Manager's daily review](#j13-managers-daily-review) | Manager |
| J14 | [Correcting a signed record](#j14-correcting-a-signed-record) | Doctor, Manager (medical director) |
| J15 | [Doctor application to first day](#j15-doctor-application-to-first-day) | Doctor applicant, Manager |
| J16 | [Patient sign-up with a mobile number](#j16-patient-sign-up-with-a-mobile-number) | Patient |

---

### J1. A complete visit: online booking to follow-up

**Trigger:** Mohamed needs a cardiology consultation.

```mermaid
sequenceDiagram
    actor P as Patient
    participant S as ClinicFlow
    actor R as Receptionist
    actor N as Nurse
    actor D as Doctor
    P->>S: Choose branch, service, doctor, slot
    S-->>P: Price; booking Confirmed
    P->>S: Pay a deposit through Paymob (optional)
    S-->>P: Confirmation (phone, email or both, as chosen)
    S-->>P: Reminder 24 h before
    P->>R: Arrives at the branch
    R->>S: Check in (verify phone, insurance, balance, deposit)
    S-->>N: Patient appears in "waiting for triage"
    N->>S: Record vitals, chief complaint, allergies
    S-->>D: Patient ready, triage summary
    D->>S: Call patient (InProgress), write encounter
    D->>S: Diagnoses, prescription, order, follow-up in 2 weeks
    D->>S: Sign encounter, complete visit
    S-->>R: Invoice ready at checkout
    R->>S: Collect the fee minus any deposit (cash/card), book follow-up
    S-->>P: Receipt, prescription, visit summary in portal
```

| # | Actor | Action | System | Status / FR |
|---|---|---|---|---|
| 1 | Patient | Searches by specialty and branch, picks Dr. Ahmed, service "Cardiology consultation" | Shows slots in branch-local time, the price from the price list, and the optional deposit amount | FR-PAT-01, FR-PAT-02, FR-SVC-04 |
| 2 | Patient | Books the slot | Books atomically (no double booking); the appointment is confirmed at once. Offers an optional deposit | `Confirmed`, FR-APT-02 |
| 3 | Patient | Chooses to pay the deposit through Paymob (or skips it) | Paymob's hosted page takes the card or wallet payment; the webhook records it against the appointment. Skipping changes nothing: the booking stays confirmed | FR-APT-08, FR-BIL-06 |
| 4 | System | — | Sends the confirmation, then a reminder 24 h before, by phone (WhatsApp, or SMS when on), email or both as the patient chose, in their language | FR-NTF-03, FR-NTF-04, FR-APT-09 |
| 5 | Receptionist | Searches the patient by phone, clicks **Check in** | Shows identity, outstanding balance, any paid deposit, insurance policy status. Creates the queue entry and an empty encounter | `CheckedIn`, FR-FD-03, FR-EMR-01 |
| 6 | Nurse | Opens "waiting for triage", picks Mohamed | Records vitals (BP, pulse, temperature, weight, height, SpO₂), chief complaint, allergy check | FR-EMR-02 |
| 7 | Doctor | Sees the queue with triage done, clicks **Call** | Assigns the room, shows the patient on the waiting-room display by ticket number | `InProgress`, FR-FD-05, FR-FD-06 |
| 8 | Doctor | Reviews history, writes SOAP notes from the cardiology template, adds ICD-10 diagnoses, prescribes, orders an echo, adds a follow-up recommendation | Checks prescriptions against recorded allergies; saves the draft continuously | FR-EMR-03…07, FR-EMR-12 |
| 9 | Doctor | Signs and clicks **Complete** | Locks the encounter; adds the services performed to the visit; creates the invoice | `Completed`, encounter `Signed`, FR-EMR-09, FR-BIL-01 |
| 10 | Receptionist | Opens checkout | Shows the invoice with any deposit already deducted; she collects the remainder in cash or by card into her open cash session and prints the receipt. No accountant is involved | invoice `Paid`, FR-BIL-05, FR-BIL-07, FR-BIL-14 |
| 11 | Receptionist | Books the follow-up suggested by the doctor | Suggests slots with the same doctor around the recommended date | FR-APT-11 |
| 12 | Patient | Opens the portal at home | Sees the visit summary, the prescription (PDF) and the receipt | FR-PAT-04, FR-PAT-05 |

**Exceptions:**
- No deposit paid → nothing happens; the booking stays confirmed and the full fee is collected at the desk.
- The Paymob payment fails or is abandoned → the booking stays confirmed; the patient can retry from the appointment.
- Before EMR ships (P4), steps 6–9 happen on paper and the doctor clicks **Complete**; the invoice is built from the booked service.
- An allergy conflict on the prescription → a warning the doctor must acknowledge; the acknowledgement is recorded.
- The doctor forgets to sign → the encounter appears in "unsigned encounters" for the doctor and on the manager dashboard after 24 h (FR-RPT-09).

---

### J2. Phone booking

**Trigger:** a patient calls the branch.

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Receptionist | Searches by phone number | Finds the patient, or offers **Register** with duplicate detection (name + date of birth + phone) | FR-FD-01, FR-FD-02 |
| 2 | Receptionist | Registers a new patient: name, phone, gender, date of birth, optional national ID | Creates a `PatientRecord` with an MRN. Sends a phone message with a portal invitation (optional) | FR-FD-02 |
| 3 | Receptionist | Picks the service; the system shows the first available slots across the doctors who offer it | Slots respect service duration (a 40-minute procedure takes two 20-minute slots) | FR-SCH-09, FR-APT-03 |
| 4 | Receptionist | Books | Same atomic booking as online; source = `Phone`; `Confirmed` immediately. The patient pays at the desk on arrival | FR-APT-03 |
| 5 | System | — | Sends the confirmation by the patient's chosen channel (phone for a new desk-registered patient) | FR-NTF-03, FR-NTF-04 |

---

### J3. Walk-in patient

**Trigger:** a patient arrives without a booking.

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Receptionist | Finds or registers the patient | — | FR-FD-01, FR-FD-02 |
| 2 | Receptionist | Clicks **Walk-in**, picks the service, and a doctor or "first available" | Shows the estimated wait per doctor from the queue. If there is a free slot now, books it; otherwise creates an appointment with no slot, flagged `WalkIn`, placed in the queue by the organization's queue policy | FR-APT-04 |
| 3 | — | — | The appointment starts at `CheckedIn` | `CheckedIn` |
| 4+ | | Continues as J1 from step 6 | | |

**Rules:** walk-ins never displace a checked-in patient with a booking whose slot time has arrived. A Manager can cap walk-ins per doctor per day (setting).

---

### J4. Reschedule

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Patient (portal) or Receptionist | Opens the appointment, clicks **Reschedule** | Patient: allowed until the cancellation window (default 2 h). Receptionist: any time before check-in | FR-APT-05 |
| 2 | Same | Picks a new slot | Books the new slot first, then marks the old appointment `Rescheduled` with a link to the new one, in one transaction. A deposit moves to the new appointment | old `Rescheduled`, new `Confirmed` |
| 3 | System | — | Notifies the patient and the doctor | FR-NTF-03 |

---

### J5. Doctor leave and bulk reschedule

**Trigger:** Dr. Ahmed will attend a conference next Tuesday.

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Doctor | Requests leave for Tuesday, all day | Shows how many appointments are affected (e.g. 14) | FR-SCH-06 |
| 2 | Manager | Approves | Blocks all of the doctor's Tuesday slots; lists the 14 affected appointments as a **reschedule worklist** | FR-SCH-06 |
| 3 | Manager / Receptionist | For each appointment, picks: move to another doctor (same service), move to another day, or cancel | Offers the best alternative automatically (same doctor's next free slot; another doctor with the service at the same time) | FR-APT-05 |
| 4 | System | — | Notifies each patient with the new time, or a cancellation with a rebooking link. Deposits move or are refunded according to the choice | FR-NTF-03, FR-BIL-08 |
| 5 | Manager | Sees the worklist go to zero | — | — |

**Emergency leave** (same day): the same flow, started by the Manager. Checked-in patients are moved to another doctor's queue or offered a new time.

---

### J6. Late arrival and no-show

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | System | The slot start time passes and the patient has not checked in | Marks the appointment as "late" in the calendar (display only) | FR-FD-04 |
| 2 | Receptionist | Patient arrives within the grace period (setting, default 15 min) | Normal check-in; the queue places them after patients already waiting for that doctor | FR-FD-03 |
| 3 | System | Grace period plus slot length passes | Suggests **Mark no-show** to the desk (not automatic, so staff stay in control) | FR-APT-07 |
| 4 | Receptionist | Marks no-show | Status `NoShow`; frees nothing (the slot time is gone); applies the no-show fee if the organization uses one (keeps the deposit, or posts a charge to the patient's balance) | `NoShow`, FR-BIL-13 |
| 5 | System | End of day | Any appointment still `Confirmed` with a past slot is listed for the desk to resolve; nothing is changed silently | FR-APT-07 |

---

### J7. Insurance visit

```mermaid
sequenceDiagram
    actor R as Receptionist
    participant S as ClinicFlow
    actor D as Doctor
    actor A as Accountant
    participant I as Insurer / TPA
    R->>S: Check in; select patient policy
    S-->>R: Policy valid? Plan covers service? Pre-auth needed?
    R->>I: Verify eligibility (phone / portal)
    R->>S: Record eligibility result + reference
    D->>S: Encounter; orders a procedure that needs pre-auth
    S-->>A: Pre-auth request queued
    A->>I: Send request (portal / email)
    A->>S: Record approval number, approved amount
    R->>S: Checkout: patient pays co-pay only
    S-->>A: Insurer share on the claim worklist
```

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Receptionist | At check-in, selects the patient's policy (or captures a new card: payer, plan, member number, expiry, photo) | Validates the expiry date; shows the plan's coverage for the booked service and whether pre-auth is required | FR-INS-03, FR-INS-04 |
| 2 | Receptionist | Verifies eligibility with the payer by phone or portal | Records the result (eligible / not), the reference number and who checked | FR-INS-04 |
| 3 | Doctor | Orders a service that needs pre-authorization | Creates a pre-auth request with the diagnosis codes and the requested service | FR-INS-05 |
| 4 | Accountant / Receptionist | Submits it to the payer, records the decision | `Approved` with an approval number and amount, or `Rejected` (the patient may then pay cash) | FR-INS-05 |
| 5 | System | The visit completes | The invoice splits each line: patient share (co-pay, non-covered items) and payer share | FR-INS-06, FR-BIL-04 |
| 6 | Receptionist | Collects the patient share only | The payer share stays open as a receivable from the payer | FR-BIL-05 |
| 7 | — | Continues in J10 | | |

**Exceptions:** an expired policy or an uncovered service → the system shows the full price as patient share, and the receptionist tells the patient before the visit.

---

### J8. Refund

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Receptionist | A patient cancelled a prepaid procedure; clicks **Request refund** on the payment, enters the amount and reason | Validates the amount ≤ the refundable balance | FR-BIL-08 |
| 2 | Accountant or Manager | Opens pending approvals, approves | The threshold decides who may approve ([02 §7](02_ROLES_AND_PERMISSIONS.md#7-approvals-four-eyes-rules)); requester ≠ approver | FR-BIL-08 |
| 3 | Cashier / System | Pays out | Cash: from the current cash session. Card/online: through the gateway refund API. The invoice and patient balance update | FR-BIL-06, FR-BIL-09 |

---

### J9. End-of-day cash close

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Receptionist | At the start of the shift, opens a cash session on drawer "Desk 1" with a float of 500 EGP | One open session per user and per drawer | FR-BIL-09 |
| 2 | Receptionist | Collects payments during the day | Each cash payment and cash refund is tied to the session | FR-BIL-05 |
| 3 | Receptionist | At the end of the shift, counts the cash and enters the total (optionally per denomination) | Shows expected cash = float + cash in − cash out; computes the variance. Card and online totals are listed for the record | FR-BIL-09 |
| 4 | Manager (or Accountant, if the clinic has one) | Reviews the closes of all drawers at the branch | A variance above the threshold needs a note and an approval | FR-BIL-09, FR-BIL-15 |
| 5 | System | — | Daily close report per branch: visits, invoices issued, collections by method, refunds, unbilled completed visits | FR-BIL-15, FR-RPT-07 |

---

### J10. Monthly claims cycle

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Accountant | On the 1st, opens **Claims → New batch**, picks the payer and the period | Collects every invoice line with an open payer share in the period, with the diagnosis codes, service codes and pre-auth numbers | FR-INS-07 |
| 2 | Accountant | Reviews the warnings (missing pre-auth, missing diagnosis, expired policy) and fixes them | Lines with errors cannot be submitted | FR-INS-07 |
| 3 | Accountant | Exports the claim in the payer's format (Excel/CSV/PDF) and submits it on the payer's portal | Status `Submitted`, submission date and reference recorded | FR-INS-08, FR-INS-09 |
| 4 | Accountant | Receives the remittance: paid amounts and rejections per line | Records it; status becomes `Paid`, `PartiallyApproved` or `Rejected` per claim; rejected lines show reasons | FR-INS-10 |
| 5 | Accountant | For each rejected line: fix and resubmit in the next batch, or write off, or bill the patient | Resubmission links to the original claim line; write-off needs `refunds.approve`-level approval | FR-INS-11 |
| 6 | Accountant | Checks payer aging | Report: outstanding by payer, 0–30 / 31–60 / 61–90 / 90+ days | FR-INS-12 |

---

### J11. Patient views records and invoices

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Patient | Opens **My visits** | Lists past visits per branch and doctor | FR-PAT-05 |
| 2 | Patient | Opens a visit | Shows the patient-facing summary: diagnoses (plain names), prescriptions, orders, instructions. Not the doctor's internal notes | FR-EMR-13 |
| 3 | Patient | Downloads the prescription and the receipt | PDFs with the clinic header and a verification code | FR-BIL-07, FR-EMR-05 |
| 4 | Patient | Uploads an outside lab result before the next visit | Stored as a patient-uploaded attachment, visible to the treating doctor | FR-PAT-06, FR-EMR-08 |

---

### J12. Staff onboarding and offboarding

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Manager | Invites a new staff member (e.g. a nurse, or a doctor hired outside the application pipeline in J15): email, name, role, branches, and for a doctor the specialty and services offered | Sends an invitation link valid for 72 h | FR-STF-01, FR-STF-05 |
| 2 | Doctor | Opens the link, sets a password, enrols MFA | Activates the staff member | FR-STF-02, FR-STF-07 |
| 3 | Manager | Creates the doctor's schedules per branch | Slots appear for the rolling window | FR-SCH-01, FR-SCH-02 |
| 4 | Manager | Later: the doctor leaves the clinic. Clicks **Deactivate**, picks an end date | Shows future appointments to reassign (J5 worklist). On the end date: sessions revoked, account inactive, records keep the author's name | FR-STF-04 |

---

### J13. Manager's daily review

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Manager | Opens the dashboard at 8 am | KPI tiles and the four charts (appointments by outcome, weekly no-show rate, daily revenue by method, visits per doctor); today per branch: booked vs capacity, doctors on leave | FR-RPT-11, FR-RPT-01 |
| 2 | Manager | During the day | Live queue length and median wait per doctor; alerts when a wait exceeds 30 minutes | FR-RPT-04 |
| 3 | Manager | Approves pending discounts, refunds and leave | One approvals inbox | FR-BIL-03, FR-BIL-08, FR-SCH-06 |
| 4 | Manager | Weekly | Utilisation per doctor, no-show rate, revenue per service, unsigned encounters | FR-RPT-02, FR-RPT-03, FR-RPT-05, FR-RPT-09 |

---

### J14. Correcting a signed record

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Doctor | Notices a wrong dose in a signed encounter | The encounter is read-only | FR-EMR-09 |
| 2 | Doctor | Adds an addendum: "Dose corrected to 5 mg", and signs it | The addendum is shown under the original with time and author; the original text is untouched | FR-EMR-10 |
| 3 | Doctor | Or: the encounter was written for the wrong patient. Requests **Void** with a reason | Pending approval by the medical director | FR-EMR-11 |
| 4 | Manager (medical director) | Approves | Encounter `Voided`: hidden from normal views, kept for audit, with both identities and the reason | FR-EMR-11, FR-AUD-01 |

---

### J15. Doctor application to first day

**Trigger:** Dr. Laila, a dermatologist, wants to join the clinic.

```mermaid
sequenceDiagram
    actor A as Applicant
    participant S as ClinicFlow
    actor M as Manager (recruitment.manage)
    A->>S: Sign up (phone + email verified), apply with CV (+ cover letter)
    S-->>A: Email: application received (Applied)
    M->>S: Review CV; move to Interview with date, place, interviewer
    S-->>M: Preview of the interview email
    M->>S: Confirm
    S-->>A: Email (+ phone message): interview invitation with the details
    A->>M: Attends the interview
    M->>S: Move to Accepted with start date and branch
    S-->>A: Email: accepted, start date and branch
    S->>S: On the start date: activate the Doctor staff member
    M->>S: Create schedules, publish the profile
```

| # | Actor | Action | System | Status / FR |
|---|---|---|---|---|
| 1 | Applicant | Opens **Join as a doctor**, signs up with phone and email, and verifies both | Sends a one-time code by WhatsApp (or SMS) and an email link; the account has no role | FR-ACC-01, FR-ACC-02, FR-REC-01 |
| 2 | Applicant | Fills in specialty, license number, years of experience, preferred branch; uploads the CV (required) and a cover letter (optional) | Checks type and size, scans the files, stores them outside the database | `Applied`, FR-REC-01 |
| 3 | System | — | Sends the "application received" email in the applicant's language | FR-REC-04 |
| 4 | Manager | Opens **Applications**, filters by specialty, opens Dr. Laila's, downloads the CV | Logs the download in the audit trail | FR-REC-07 |
| 5 | Manager | Clicks **Invite to interview**, enters date and time, branch, interviewer and a note ("bring your syndicate card") | Validates the values; shows the rendered email with the placeholders filled in | FR-REC-03, FR-REC-04 |
| 6 | Manager | Confirms | Moves the application and records the variables in its history; sends the email (and a phone message if chosen) through the outbox | `Interview` |
| 7 | Applicant | Sees the interview details in **My application**; attends | — | FR-REC-06 |
| 8 | Manager | Clicks **Accept**, enters the start date (1 November) and the branch | Shows the rendered acceptance email; on confirm, creates a `Doctor` staff member, `Inactive` until the start date, and a hidden doctor profile | `Accepted`, FR-REC-05 |
| 9 | System | 1 November | Activates the staff member; the doctor can now log in to the staff app and enrol MFA | FR-REC-05, FR-STF-07 |
| 10 | Manager | Creates the doctor's schedules and publishes the profile | Slots appear; the doctor shows in public search | FR-SCH-01, FR-STF-05 |

**Exceptions:**
- The manager rejects at step 4 or after the interview → `Rejected`, with an optional message in the email. The files are deleted after the retention period (FR-REC-08).
- The applicant withdraws before acceptance → `Withdrawn`; the manager sees it in the list.
- The email fails to send → the stage change stands; the failed delivery shows in the history and is retried.

---

### J16. Patient sign-up with a mobile number

| # | Actor | Action | System | FR |
|---|---|---|---|---|
| 1 | Patient | Opens **Sign up** in Arabic, enters name, mobile number `010…` and a password; leaves email empty | Validates the Egyptian mobile format, stores it as `+2010…`; checks it isn't already used (without revealing it if it is) | FR-ACC-01 |
| 2 | System | — | Sends a 6-digit code by WhatsApp (by SMS when WhatsApp can't reach the number and SMS is on), valid 5 minutes | FR-ACC-02 |
| 3 | Patient | Enters the code | Verifies it (at most 5 attempts); marks the phone verified; saves the preferred language `ar`; signs the patient in | FR-ACC-02, FR-ACC-06 |
| 4 | Patient | Books an appointment | Allowed because the phone is verified; confirmation and reminders go by phone message (no email on file) | FR-APT-02, FR-NTF-04 |
| 5 | Patient | Later adds an email and picks **both** channels in the profile | Sends a confirmation link; once confirmed, messages go by phone and email | FR-ACC-05, FR-NTF-04 |

**Exceptions:** the code expires or is mistyped 5 times → the patient asks for a new one after 60 s; daily caps per number and per IP stop abuse ([08 §2](08_SECURITY_PRIVACY_COMPLIANCE.md#2-threats-and-controls)).
