# ADR 0008: One platform for many clinics, with per-organization module entitlements

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-10-26 |
| **Deciders** | Owner and only developer |
| **Amends** | [ADR 0001](0001-single-organization-tenant-ready.md) decisions 1 and 8 (exactly one organization; no onboarding, platform operator or tenant resolution in V2) |

## Context

ClinicFlow will be offered to many clinics, not just one. Clinics differ in size and need: a single-doctor practice wants booking, a calendar and reminders, while a multi-branch polyclinic also wants front desk, billing, EMR, insurance and reports. Price has to follow what a clinic uses, or the product is too expensive for small clinics, which are most of the Egyptian market.

Two questions follow:

1. **How are clinics hosted?** Either one deployment and database per clinic, or one platform that every clinic logs into.
2. **How does one codebase serve clinics that bought different modules?** Separate builds per clinic, or one build whose behaviour each clinic's subscription switches.

### What competitors do (research, 2026-10-26)

Figures come from vendor pages and third-party pricing guides that disagree with each other. They show the patterns, not reliable prices.

| Product | Market | Pricing basis | How features are packaged |
|---|---|---|---|
| [Tabbaba](https://tabbaba.com/en/clinic-management-software-egypt/) | Egypt | **Per clinic, "not per doctor"**; Starter from 799 EGP/month | Tiers: Starter (appointments, patients, invoices, basic WhatsApp reminders) → Clinic (WhatsApp booking, AI, queue display, full reports); dental workflow |
| [Clintee](https://clintee.com/clinic-management-software-egypt) | Egypt | Free plan + paid tiers | Free forever: scheduling, EMR, clinic website; premium tiers for larger practices |
| [ClinicGateway](https://clinicgateway.ae/blog/best-clinic-management-software-egypt-2025/) | Egypt, KSA | From 2,500 EGP/month (vendor's claim) | One bundle; stresses Arabic, WhatsApp, the Tax Authority e-receipt and mixed cash/insurance patients as Egyptian needs |
| [Vezeeta](https://www.vezeeta.com/en/generic/aboutus) | Egypt, KSA, Jordan | Subscription + a fee per marketplace booking ([Dealroom](https://dealroom.co/companies/vezeeta/)) | Booking marketplace plus practice-management tools |
| [Cliniko](https://pabau.com/blog/cliniko-pricing/) | Global | **Bands by practitioner count** ($45 for 1 … $395 for 26–200) | One feature set for everyone; **SMS billed per message** (~$0.10) |
| [Jane](https://pricingsaas.com/companies/jane) | Global | Per plan, extra per practitioner | **3 plans + 4 add-ons**: insurance billing, group telehealth, AI scribe sold separately |
| [SimplePractice](https://www.simplypsychology.com/articles/simplepractice-review-2026) | US | 3 tiers, per clinician on the top tier | Insurance billing, telehealth and the client portal are locked to the middle tier; advanced reporting and extra staff roles to the top tier |
| [Practo Ray](https://www.capterra.com/p/167778/Ray/) / [Insta HMS](https://www.instahms.com/pricing-ksa) | India, KSA | **Per user per month** | Clinic vs hospital tiers; modules such as financial accounting sold separately |
| [Odoo](https://capterra.com/p/135618/Odoo/pricing/) | Global ERP | Per user | One monolith with installable apps; the free tier allows one app |

What they have in common:

- **Every one of them is a shared SaaS platform.** None asks a small clinic to run its own server.
- **Packaging is tiers plus add-ons,** with per-clinic deals on top. The features most often kept for higher tiers or sold separately are insurance billing, telehealth, the patient portal and online booking, advanced reporting, multi-branch, the queue display and AI features.
- **Price grows with size:** per clinic, per practitioner band, per user, or per branch. In Egypt the local vendors advertise **per-clinic** pricing as a selling point against per-doctor pricing.
- **Messaging is metered.** SMS and WhatsApp cost money per message, so they are sold as usage or bundles, not as a flat feature.

### What ClinicFlow already has

- **Tenancy in the schema** (ADR 0001, P0 PR 5): every clinic-owned row carries `OrganizationId`, global query filters isolate organizations, and a save interceptor stamps and checks it. Integration tests seed a second organization to prove isolation.
- **Modules are already defined** ([04 §3](../04_FUNCTIONAL_REQUIREMENTS.md#3-module-index)), and the permission catalog is already grouped by module ([02 §5](../02_ROLES_AND_PERMISSIONS.md#5-permission-catalog)).
- **Every request is already authorized by a permission check** (ADR 0002, `[HasPermission]`).
- **Modules are already loosely coupled through outbox events** (06 §5), so one module can react to another without calling it.
- **One deployable modular monolith** (ADR 0006), with no microservices.

## Decision

### Hosting

1. **ClinicFlow is one multi-tenant platform.** One deployment and one database serve every clinic. Each clinic is an `Organization`; isolation stays row-level, as built in P0. There is no database per clinic, no schema per clinic and no build per clinic.
2. **A dedicated deployment stays possible, but isn't the default.** A large customer who demands isolation can get the same image with its own database and one organization, at an enterprise price. No code may assume either model.

### Modules and plans

3. **The module catalog is code; plans are data.**
   - Module codes are constants in `Shared/Authorization/Modules.cs` (`Modules.Billing = "BIL"`, …), matching [04 §3](../04_FUNCTIONAL_REQUIREMENTS.md#3-module-index).
   - **Every permission belongs to exactly one module.** The "Module" column of 02 §5 becomes authoritative, and the existing `PermissionMatrixTests` also checks it.
   - Plans, the modules and limits they include, prices and per-clinic deals are **database rows**, managed by the platform operator. Code never branches on a plan name (`if (plan == "Pro")`).
4. **Core modules are always on; everything else is an add-on.**
   - **Core:** ORG, STF, ACC, SVC, SCH, APT, NTF (in-app and email) and AUD. Without them the product isn't a clinic system.
   - **Add-ons:** FD, BIL, EMR, INS, RPT (beyond the dashboard), PAT, REC and BOT, plus paid integrations such as Paymob online deposits.
   - **Phone channels** ([06 §9.2](../06_TECHNICAL_DESIGN.md#92-whatsapp-and-sms-phone-channels)) are switched on or off for the whole platform by configuration. Clinics use them through metered quotas (decision 5): **WhatsApp** messages are included in every plan, and **SMS** is an add-on, for clinics whose patients need it. **Verification codes** (sign-up, phone change, password reset) are a platform cost of the core ACC module, never charged to a clinic or blocked by its quota.
   - **Dependencies between add-ons** are declared in code (INS needs BIL, `reports.financial` needs BIL, BOT needs PAT). Enabling a module enables its dependencies; disabling one that another enabled module needs is refused.
5. **Limits are part of the entitlement, not just on/off modules.** There are two kinds:
   - **Counted limits:** active branches, active doctors, staff users. They are checked at the write that would exceed them (inviting staff, activating a doctor, creating a branch).
   - **Metered quotas:** WhatsApp and SMS messages per month, counted per channel (verification codes excluded), file storage, chatbot conversations. They are counted in a `UsageRecord(OrganizationId, Meter, Period, Count)` and checked before the send or upload.
6. **The data model:**
   ```
   Plan(Id, Code, Name, IsPublic)                                  platform-owned
   PlanModule(PlanId, ModuleCode)
   PlanLimit(PlanId, LimitKey, Value)
   OrganizationSubscription(OrganizationId, PlanId, Status, TrialEndsAt, CurrentPeriodEnd)
   OrganizationModuleOverride(OrganizationId, ModuleCode, Enabled) add-ons, deals, a module switched off
   OrganizationLimitOverride(OrganizationId, LimitKey, Value)      extra doctors, branches, message bundles
   UsageRecord(OrganizationId, Meter, Period, Count)
   ```
   - **Effective modules** = core ∪ plan modules ∪ enabled overrides − disabled overrides, closed over dependencies.
   - **Effective limits** = the override if one exists, otherwise the plan's value.
   - Changes to plans, subscriptions and overrides are audited (FR-AUD-01).

### Enforcement

7. **`IEntitlements`** (in `Services.Abstraction`) answers `IsEnabledAsync(module)`, `LimitAsync(key)` and `TryConsumeAsync(meter, n)` for the current organization. It is computed once per organization and cached in memory, and invalidated when any of that organization's rows change. It is the only place that reads the subscription tables.
8. **The permission check enforces modules.** `PermissionAuthorizationHandler` grants a permission only when one of the user's roles grants it **and** the permission's module is enabled for the user's organization. Every `[HasPermission]` endpoint is therefore gated with no extra code.
9. **A second line of defence on add-on controllers.** Each add-on module's controllers carry `[RequiresModule(Modules.X)]` (module folders, ADR 0006). An architecture test fails when an action in an add-on module folder lacks it, as `AuthorizationTests` already does for `[Authorize]`.
10. **Distinct errors so the frontend can say "upgrade", not "forbidden".**
    - A disabled module returns **403** with the code `Subscription.ModuleNotEnabled`.
    - An exceeded limit or quota returns **403** with `Subscription.LimitReached` (naming the limit).
    - Both go into the contract's error catalog.
11. **Cross-module behaviour runs through events, and a handler checks its own module.** An outbox handler that belongs to an add-on does nothing when that module is off for the event's organization. For example, the billing handler for `AppointmentCompleted` creates an invoice only when BIL is on. The module that raises the event never knows who listens. Background jobs process only the organizations whose module is enabled.
12. **The backend decides; the frontend only hides.** `GET /api/v2/organization/entitlements` returns the effective modules, limits, current usage and subscription status. The frontend uses it to hide menus, routes and buttons, and still handles the 403 codes from decision 10.
13. **A lapsed subscription never deletes data.**
    - **`Trial` / `Active`:** full use.
    - **`PastDue`:** full use for a grace period (an organization setting).
    - **After the grace period, or `Cancelled`:** **read-only**. Reads and exports work; writes are refused, except signing in, paying the subscription and exporting.
    - **A module removed by a downgrade** becomes read-only for that module the same way. Medical and financial records can't simply disappear, and the clinic must be able to take its data with it.

### Tenant resolution

14. **Staff:** the organization comes from the signed-in user's staff membership (`StaffMember(OrganizationId, UserId)`, P1). It is carried as a claim in the access token. A person who works for several clinics chooses one, which issues a new token. `SingleOrganizationTenantContext` is replaced by a context that reads the claim.
15. **Patients and public pages:** a patient account is global (ADR 0001 decision 4). The clinic is chosen from the URL of its booking page or portal. Whether that is a sub-domain or a path is open question Q18. Patient-facing features of a clinic follow that clinic's PAT module.

### When

16. **P1** builds the entitlement foundation, while there is still only one module's worth of endpoints to gate:
    - the module catalog and the module of each permission
    - the subscription tables, seeded with one plan that has every module
    - `IEntitlements`, the permission-handler check, `[RequiresModule]` and its architecture test
    - the entitlements endpoint and the two error codes
    - counted limits for branches, doctors and staff, and the phone-message meters (the phone channels arrive in P1, switched off)
    - tenant resolution from staff membership (decision 14)
17. **A new platform phase** delivers everything needed before a second paying clinic is onboarded:
    - the **platform operator role** and console: organizations, plans, overrides, usage
    - **clinic onboarding**
    - **subscription billing of clinics** (the V3 row of [01 §5](../01_VISION_AND_SCOPE.md#5-v2-versus-v3) moves into V2)
    - lapse handling (decision 13)
    - per-organization data export
    - public clinic addressing (decision 15)

    Its place in the phase order is open question Q20. Because modules can be switched on later, the product can be sold to small clinics as soon as the core modules ship, before EMR or insurance exist.

## Consequences

- **Positive:** small clinics become affordable. They share hosting, the database server and operations instead of paying for their own, which is the cost that dominates at a few hundred EGP a month.
- **Positive:** one build, one schema and one set of migrations. A fix or feature reaches every clinic in one deployment, which suits a solo developer.
- **Positive:** pricing and packaging change without a release. A new plan, a promotion or a deal for one clinic is just rows.
- **Positive:** a clinic upgrades instantly, and a sales trial can switch on everything for a limited time.
- **Positive:** most of the work is already done. Tenancy, permissions, module folders and the outbox were built in P0 for this shape.
- **Negative:** one bug or one bad migration affects every clinic at once.
  - After V2 GA, schema changes must be backward compatible again (expand → migrate → contract), because a live multi-clinic database can't wait for a coordinated stop.
  - Releases need a staging run with production-like data.
- **Negative:** isolation is the whole security model. A query that bypasses the filter leaks one clinic's patients to another. Mitigations:
  - the existing filters and stamp interceptor
  - the second-organization integration tests, extended to every new module
  - the rule that raw SQL and `ExecuteUpdate`/`ExecuteDelete` add the organization predicate themselves
- **Negative:** a heavy clinic can slow the others (noisy neighbour). Reports and exports need limits and, later, a read replica.
- **Negative:** restoring one clinic's data is harder than restoring a dedicated database. A per-organization export (decision 17) is the answer for both portability and recovery.
- **Negative:** every add-on adds an entitlement check, a dependency entry and tests where it is both on and off. The architecture test and the permission-handler check keep this from being forgotten.
- **Risk:** legal requirements on hosting location and processing sensitive data (Q3, [08](../08_SECURITY_PRIVACY_COMPLIANCE.md)) now apply to a platform holding many clinics' records, not one clinic's. The legal review must cover the platform.

## Open questions

| # | Question | Needed by |
|---|---|---|
| Q18 | Public clinic address: a sub-domain (`<clinic>.clinicflow.app`) or a path (`clinicflow.app/c/<clinic>`)? Sub-domains look better and are more work (wildcard TLS, DNS) | Platform phase |
| Q19 | Pricing basis and numbers. Local competitors advertise **per clinic**; international ones charge per practitioner band. The proposal is a per-clinic base by plan that includes 1 branch and N doctors, with extra doctors, extra branches and message bundles sold as add-ons | Before the first sale |
| Q20 | Where the platform phase goes in the phase order. The proposal is after P3, so a clinic can be sold the core modules plus desk billing | P1 review |
| Q21 | How clinics pay the platform: Paymob recurring card payments, bank transfer with manual activation, or both | Platform phase |
| Q22 | The grace period, and what read-only still allows (printing past invoices, patients seeing past appointments) | Platform phase |
| Q23 | Is the Tax Authority e-receipt (FR-BIL-16, *Won't*) needed to compete? It appears in local competitors' offers. (WhatsApp, the other half of this question, was **answered on 2026-10-26**: it is the intended primary phone channel, [06 §9.2](../06_TECHNICAL_DESIGN.md#92-whatsapp-and-sms-phone-channels)) | P3 (with Q4) |

## Initial packaging (a proposal, not a decision)

These are example plans to test the model against, not final prices or tiers.

| | Starter | Clinic | Full |
|---|---|---|---|
| Core (accounts, staff, services and prices, scheduling, appointments, in-app and email notifications, dashboard, audit) | ✓ | ✓ | ✓ |
| Front desk and queue display (FD) | | ✓ | ✓ |
| Billing at the desk (BIL) | | ✓ | ✓ |
| Patient portal and online booking (PAT) | | ✓ | ✓ |
| Operational reports (RPT) | | ✓ | ✓ |
| Clinical records (EMR) | | | ✓ |
| Insurance (INS) | | | ✓ |
| Financial and clinical reports (RPT) | | | ✓ |
| Branches / doctors included | 1 / 2 | 1 / 5 | 3 / 15 |
| WhatsApp messages included per month (confirmations, reminders) | a small quota | a larger quota | a larger quota |
| **Add-ons on any plan** | extra WhatsApp messages, SMS (when the platform has it switched on), extra doctors, extra branches, recruitment (REC), Paymob online deposits, chatbot (BOT, per conversation) | | |

## Documents to update when this is accepted

- **ADR 0001:** mark it as amended by this ADR.
- **01 §5:** the multi-clinic platform and subscription billing of clinics move from V3 into V2.
- **02 §5:** the module column becomes authoritative; add the platform operator role and its permissions.
- **04:** a new module, **PLT** (platform and subscriptions), with its FRs.
- **05:** the entities of decision 6.
- **06 §3–§4:** entitlement enforcement and tenant resolution.
- **07** and **API_CONTRACT.md:** the entitlements endpoint and the two error codes.
- **10:** P1 deliverables, the new platform phase, Q18–Q23 and a decisions-log row.
- **[plans/P1.md](../plans/P1.md):** the entitlement slice of decision 16.
