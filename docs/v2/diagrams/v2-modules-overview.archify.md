# ClinicFlow V2: every module, core or add-on {#v2-modules}

ClinicFlow V2 is one platform with **16 clinic modules**: **8 core modules** that every clinic gets, and **8 add-ons** that each clinic switches on through its plan.

- The core runs a booking clinic: the clinic and its staff, patient sign-up, services and prices, scheduling, appointments, notifications and audit.[](source:modules)
- The add-ons deepen it: front desk, billing, clinical records, insurance, reports, patient portal, recruitment and the chatbot.[](source:core-addons)
- One build serves every clinic. A platform module turns add-ons, limits and message quotas on per clinic.[](source:core-addons)
- Modules arrive in phases. The foundation (P0) is done, and the core is complete after P2.[](source:phases)

@answer scope="Whole V2 as planned in docs/v2 (commit 88771d2). The core/add-on split is ADR 0008, still Proposed."
@meta locale=en repo_url=https://github.com/S3d0o/ClinicFlow-Backend.git repo_revision=88771d2a6337b0d00bedfb017f27b82a960f7c15
@source modules title="04 Functional requirements: module index" path=docs/v2/04_FUNCTIONAL_REQUIREMENTS.md lines=26:47
@source scope title="01 Vision and scope: in and out of scope" path=docs/v2/01_VISION_AND_SCOPE.md lines=47:79
@source core-addons title="ADR 0008: core modules and add-ons" path=docs/v2/adr/0008-multi-clinic-platform-module-entitlements.md lines=57:70
@source packaging title="ADR 0008: initial packaging (proposal)" path=docs/v2/adr/0008-multi-clinic-platform-module-entitlements.md lines=156:173
@source adr-questions title="ADR 0008: open questions" path=docs/v2/adr/0008-multi-clinic-platform-module-entitlements.md lines=145:155
@source phases title="10 Roadmap: phases at a glance" path=docs/v2/10_ROADMAP.md lines=3:27
@source dependencies title="10 Roadmap: phase dependencies" path=docs/v2/10_ROADMAP.md lines=128:150
@source channels title="06 Technical design: WhatsApp and SMS" path=docs/v2/06_TECHNICAL_DESIGN.md lines=218:244

## How do the modules fit together? {#module-map}

@diagram architecture id=module-map-diagram source=v2-modules-map.archify.json title="ClinicFlow V2 modules: core and add-ons"

@steps id=module-map-steps
| s-org | Organization | Set up the clinic: branches, rooms, working hours and holidays. | module-map-diagram | org |
| s-stf | Staff & access | Invite staff and give each a role per branch. Recruitment feeds hired doctors in. | module-map-diagram | stf |
| s-sch | Scheduling | Turn doctor shifts into bookable slots inside branch hours. | module-map-diagram | sch |
| s-apt | Appointments | The hub: every booking, from any channel, follows one state machine. | module-map-diagram | apt |
| s-ntf | Notifications | Each appointment event sends in-app, email and WhatsApp messages. | module-map-diagram | ntf |
| s-bil | Billing & payments | An add-on: turns each visit into an invoice and takes payment at the desk. | module-map-diagram | bil |

Arrows show what one module hands to the next. Each module tag gives its code and the phase that builds it.

## What does each core module do? {#core-modules}

| Core module | What it does | Phase |
| --- | --- | --- |
| Organization (ORG) | The clinic and its branches: addresses, time zones, working hours, holidays, rooms and settings such as the cancellation window. | P1 |
| Staff & access (STF) | Staff join by invitation, get roles per branch and must use MFA. Access follows permissions, not role names. | P1 |
| Accounts & sign-in (ACC) | Patients sign up with an Egyptian mobile number and a one-time code by WhatsApp. Login works with phone or email. | P1 |
| Services & prices (SVC) | The service catalog and price list, priced per branch or per doctor. Patients see the price before booking. | P1 |
| Scheduling (SCH) | Doctor shifts per branch and room, leave, blocked slots and a rolling window of slots. | P2 |
| Appointments (APT) | One state machine for every booking: book, reschedule, cancel, check in, no-show and complete. | P0–P2 |
| Notifications (NTF) | In-app, email and phone messages from Arabic and English templates. Patients choose phone, email or both. | P1–P2 |
| Audit (AUD) | Records who changed what, when and from where on every sensitive record. Entries can never be edited. | P0–P4 |

The admin dashboard, with KPI tiles and up to four charts, also belongs to the core. Only the reports beyond it are an add-on.[](source:packaging)

## What does each add-on do, and who needs it? {#addon-modules}

| Add-on | What it adds | Who needs it | Needs | Phase |
| --- | --- | --- | --- | --- |
| Front desk & queue (FD) | Desk booking, walk-ins, check-in, a live queue board and a waiting-room screen. | Clinics with a reception desk | APT | P2 |
| Billing & payments (BIL) | Invoices from visits, cash and card at the desk, cash sessions, refunds and optional Paymob deposits. | Clinics that bill every visit | APT, SVC | P3 |
| Clinical records (EMR) | Encounters with vitals, notes, ICD-10 diagnoses, prescriptions and orders, signed and locked. | Doctors who document in the system | APT | P4 |
| Insurance (INS) | Payers, plans, policies, the co-pay split, pre-authorizations, claims and denials. | Clinics that bill insurers | BIL, EMR | P5 |
| Reports (RPT) | Doctor usage, no-show, revenue, claims and clinical-quality reports, with exports. | Managers of larger clinics | Other modules' data | P2–P6 |
| Patient portal (PAT) | Online booking with prices, deposits, invoices, visit summaries and uploads. | Clinics that take online bookings | APT | P2–P6 |
| Recruitment (REC) | Doctors apply with a CV and move through Applied → Interview → Accepted, with an email at each stage. | Clinics that hire doctors often | STF | P1 |
| Chatbot (BOT) | An Arabic and English assistant in the portal that answers clinic questions and finds slots. | Clinics with many online patients | PAT | P7 |

Enabling an add-on also enables what it needs. Insurance, for example, switches billing on.[](source:core-addons)

## What holds every module together? {#foundation}

| Piece | What it guarantees | Status |
| --- | --- | --- |
| Tenancy | Every clinic-owned row carries `OrganizationId`, and every query is filtered by it. One clinic never sees another's data. | Built in P0 {good:Done} |
| Permissions | Each endpoint needs a permission, and the service checks the record's scope: own, branch or organization. | Built in P0 {good:Done} |
| Outbox | Events are saved with the change and delivered about 2 s later, with retries. Modules react without calling each other. | Built in P0 {good:Done} |
| Audit trail | A save hook writes the audit entries in the same transaction as the change. | Built in P0 {good:Done} |
| One clock | Times are stored in UTC. Slots keep the branch's local time next to the exact instant. | Built in P0 {good:Done} |
| Phone channels | WhatsApp and SMS are switches. WhatsApp goes first; SMS is built but off. | Planned for P1 |
| Entitlements | Each clinic's plan turns add-ons, limits and message quotas on. | Proposed {caution:ADR 0008} |

WhatsApp is not free: Meta charges about $0.0036 per template message in Egypt.[](source:channels)

## When does each module arrive? {#phases-panel}

@diagram workflow id=phase-flow source=v2-phases.archify.json title="When each module arrives"

Billing comes before clinical records on purpose: taking money at the desk is part of every visit, while records need legal sign-off first.[](source:dependencies)

## How could a clinic buy it? {#plans}

| What the plan includes | Starter | Clinic | Full |
| --- | --- | --- | --- |
| Core modules and the admin dashboard | Yes | Yes | Yes |
| Front desk, billing, patient portal, operational reports | No | Yes {good:Included} | Yes |
| Clinical records, insurance, financial and clinical reports | No | No | Yes {good:Included} |
| Branches and doctors included | 1 and 2 | 1 and 5 | 3 and 15 |
| WhatsApp messages per month | Small quota | Larger quota | Larger quota |

Any plan can add extra WhatsApp messages, SMS, extra doctors or branches, recruitment, Paymob deposits and the chatbot.[](source:packaging)

@callout level=open-question title="Still to decide"
| The tiers and numbers are examples to test the model, not prices (Q19). |
| ADR 0008 is still Proposed, and the platform phase has no place in the order yet (Q20). |
| The WhatsApp provider, Meta directly or a reseller, is open (Q2). The chatbot's scope is decided in P7 (Q16). |

## What is deliberately left out? {#out-of-scope}

| Left out of V2 | Why |
| --- | --- |
| Inventory and equipment | Excluded by the business |
| Lab, radiology and pharmacy systems | V2 writes orders and prescriptions and stores results; it doesn't run a lab or a pharmacy |
| HR and payroll | Hiring doctors is the only HR process in scope |
| Telemedicine and native mobile apps | Separate product decisions; the web app stays responsive |
| Electronic links to insurers and the e-receipt system | Claims are exported and tracked by hand for now |

These come from the scope document's out-of-scope list.[](source:scope)
