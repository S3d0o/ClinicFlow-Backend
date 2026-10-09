# ClinicFlow V2: Clinic Operations

| | |
|---|---|
| **Status** | Draft for review |
| **Last updated** | 2026-10-09 |
| **Applies to** | ClinicFlow backend and frontend, contract version 2.0.0 |
| **Builds on** | V1 docs: [PROJECT_OVERVIEW.md](../PROJECT_OVERVIEW.md), [API_CONTRACT.md](../API_CONTRACT.md), [REVIEW.md](../REVIEW.md) |

V1 is a **patient-facing booking product**. Patients find doctors and book slots, doctors publish schedules, and one Admin approves doctors. The clinic itself is not modelled: each doctor carries their own address and fee, and the people who actually run a clinic (managers, receptionists, nurses, accountants) have no place in the system.

V2 turns ClinicFlow into the **operating system of one clinic organization with several branches**. It covers the full visit, from booking to the front desk, the nurse, the doctor, the cashier and the insurer. Every design choice is **tenant-ready**, so a later V3 can host many clinic organizations on one platform without a rewrite.

---

## Documents

Read them in this order:

| # | Document | Answers |
|---|---|---|
| 1 | [01_VISION_AND_SCOPE.md](01_VISION_AND_SCOPE.md) | Why V2, what is in and out, how we measure success, V2 vs V3 |
| 2 | [02_ROLES_AND_PERMISSIONS.md](02_ROLES_AND_PERMISSIONS.md) | Who uses the system, what each role needs, the permission catalog and matrix |
| 3 | [03_USER_JOURNEYS.md](03_USER_JOURNEYS.md) | How a real clinic day flows through the system, step by step |
| 4 | [04_FUNCTIONAL_REQUIREMENTS.md](04_FUNCTIONAL_REQUIREMENTS.md) | Every requirement, with an id, a priority and acceptance criteria |
| 5 | [05_DOMAIN_MODEL.md](05_DOMAIN_MODEL.md) | Entities, relationships, state machines, tenancy rule, V1 → V2 mapping |
| 6 | [06_TECHNICAL_DESIGN.md](06_TECHNICAL_DESIGN.md) | How V2 fits the existing architecture: authorization, tenancy, time zones, events, jobs, integrations |
| 7 | [07_API_DESIGN.md](07_API_DESIGN.md) | Contract 2.0.0 direction: route groups, shapes, versioning, breaking changes |
| 8 | [08_SECURITY_PRIVACY_COMPLIANCE.md](08_SECURITY_PRIVACY_COMPLIANCE.md) | Medical and payment data protection, audit, legal checklist |
| 9 | [09_MIGRATION_FROM_V1.md](09_MIGRATION_FROM_V1.md) | Moving data, API and frontend from V1 to V2; where each open REVIEW item gets fixed |
| 10 | [10_ROADMAP.md](10_ROADMAP.md) | Phases, exit criteria, risks, open questions |
| — | [adr/](adr/) | Architecture decision records for the big choices |

### Architecture decision records

| ADR | Decision |
|---|---|
| [0001](adr/0001-single-organization-tenant-ready.md) | One organization in V2, with a tenant-ready schema |
| [0002](adr/0002-permission-based-authorization.md) | Permission-based, branch-scoped authorization instead of role checks |
| [0003](adr/0003-branch-time-zones.md) | Each branch has a time zone; slots store local time plus UTC |
| [0004](adr/0004-appointment-state-machine.md) | One appointment state machine, owned by the entity |
| [0005](adr/0005-api-v2-versioning.md) | V2 endpoints live under `/api/v2`; V1 routes are frozen, then removed |

## Canonical names

These names are defined once and used verbatim in every document and, later, in code.

| Kind | Values | Defined in |
|---|---|---|
| Roles | `Admin`, `Manager`, `Receptionist`, `Nurse`, `Doctor`, `Accountant`, `Patient` | [02 §2](02_ROLES_AND_PERMISSIONS.md#2-roles) |
| Permission scopes | `Own`, `Branch`, `Organization` | [02 §4](02_ROLES_AND_PERMISSIONS.md#4-permission-model) |
| Appointment status | `Pending`, `Confirmed`, `CheckedIn`, `InProgress`, `Completed`, `Cancelled`, `NoShow`, `Rescheduled` | [05 §4.1](05_DOMAIN_MODEL.md#41-appointment) |
| Encounter status | `Draft`, `Signed`, `Voided` | [05 §4.2](05_DOMAIN_MODEL.md#42-encounter) |
| Invoice status | `Draft`, `Issued`, `PartiallyPaid`, `Paid`, `Void` | [05 §4.3](05_DOMAIN_MODEL.md#43-invoice) |
| Claim status | `Draft`, `Submitted`, `Approved`, `PartiallyApproved`, `Rejected`, `Paid`, `Closed` | [05 §4.4](05_DOMAIN_MODEL.md#44-claim) |
| Requirement ids | `FR-<MODULE>-nn`, `NFR-nn` | [04](04_FUNCTIONAL_REQUIREMENTS.md) |
| Phases | `P0` … `P6` | [10](10_ROADMAP.md) |

## Glossary

| Term | Meaning |
|---|---|
| **Organization** | The clinic business as a whole (legal entity, brand, settings). Exactly one in V2; many in V3 |
| **Branch** | One physical location of the organization, with its own address, time zone, rooms, working hours, cash drawers and invoice numbering |
| **Room** | A consultation or procedure room inside a branch. A schedule can reserve a room |
| **Staff member** | A user employed by the organization, with one or more roles, assigned to one or more branches |
| **Service** | Something the clinic sells: a consultation, a follow-up, a procedure. Has a duration and a price |
| **Price list** | The prices of services, optionally per branch or per doctor, valid from a date |
| **Walk-in** | A patient who arrives without a booking and is added to today's queue |
| **Queue** | The ordered list of checked-in patients waiting for a doctor at a branch |
| **Check-in** | The receptionist confirming the patient has arrived; it starts the visit |
| **Triage** | The nurse's intake: vital signs, chief complaint, allergies check |
| **Encounter** | The clinical record of one visit: notes, diagnoses, prescriptions, orders, attachments |
| **MRN** | Medical record number. Identifies a patient inside one organization |
| **Sign** | A doctor locking an encounter. After signing, changes are only possible as addenda |
| **Addendum** | A dated, signed correction or addition to a signed encounter |
| **Invoice** | The bill for a visit or a service, split between what the patient pays and what the insurer pays |
| **Cash session** | One cashier's shift on one cash drawer, opened with a float and closed with a count |
| **Payer** | An insurance company or a third-party administrator (TPA) that pays part of an invoice |
| **Plan** | A product of a payer, with coverage rules (covered services, co-pay, limits) |
| **Policy** | A patient's membership in a plan (member number, validity, card image) |
| **Pre-authorization** | The payer's approval for a service before it is performed |
| **Claim** | The request sent to a payer to pay its share of one or more invoices |
| **Break-glass** | Emergency access to a record outside one's normal permissions, with a mandatory reason and audit |

## Status of these documents

These are **planning** documents. Nothing here is implemented yet. When a phase ships, the matching parts move into the V1-style living docs (`PROJECT_OVERVIEW.md`, `API_CONTRACT.md` 2.0.0, `SETUP_AND_DEPLOYMENT.md`) and these files record what was decided. Open business questions are collected in [10_ROADMAP.md §5](10_ROADMAP.md#5-open-questions).
