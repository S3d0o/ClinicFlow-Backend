# 08: Security, Privacy and Compliance

V2 stores **medical records and payment information**. A breach or misuse harms patients and exposes the clinic to legal liability. This document sets the security and privacy requirements, and lists the legal points that must be **confirmed with counsel** before go-live.

> Legal statements here are planning assumptions, not legal advice. Every item marked **(confirm)** needs review by a lawyer familiar with Egyptian data-protection and healthcare regulation.

## 1. Data classification

| Class | Examples | Rules |
|---|---|---|
| **C1 Clinical (sensitive)** | Encounter notes, diagnoses, prescriptions, vitals, attachments, allergies, conditions | Clinical permissions only; read logging; encrypted columns for free-text notes; never in logs, analytics or notifications |
| **C2 Personal (identifying)** | Name, phone, national ID, date of birth, address, insurance member number, card images, doctor applicants' CVs and cover letters | Need-to-know by role; masked in logs; exports and CV downloads audited |
| **C3 Financial** | Invoices, payments, refunds, claims, cash sessions | Financial permissions; immutable after issue/close; audited |
| **C4 Secrets** | Password hashes, MFA secrets, refresh tokens, phone verification codes, provider API keys (WhatsApp, SMS, Paymob API key and HMAC secret), JWT signing key | Never logged or returned; stored hashed or in a secret store |
| **C5 Operational** | Schedules, slots, services, prices, branch data | Normal access control |

Card numbers are **never** received or stored (hosted payment page; [06 §9.1](06_TECHNICAL_DESIGN.md#91-payments)).

## 2. Threats and controls

| Threat | Control |
|---|---|
| Staff browsing records of people they know (curiosity, celebrities, relatives) | Branch scope + "treating relationship" rule; read logging; break-glass with reason; monthly access review report for the medical director; alerts on unusual volumes |
| IDOR via sequential ids (V1 C4) | Every service checks scope through `IAccessGuard`; integration matrix tests per endpoint |
| Account takeover of staff | MFA (TOTP) required; lockout (V1); session revocation on deactivation; password policy aligned across registration and reset (M13) |
| Endpoint accidentally public (V1 C1) | Fallback authorization policy and the explicit-attribute test, **already in V1**; V2 adds an allowlist of the anonymous endpoints that the test compares against |
| Verification code brute force | 6-digit codes, 5 attempts per code, 5-minute expiry, codes stored hashed and compared in constant time, `Otp` rate limit per IP |
| Message pumping / cost abuse (bots requesting codes to premium numbers; WhatsApp templates and SMS both cost money) | Egyptian mobile numbers only; 60 s resend cooldown; daily caps per number and per IP; a daily spend alert on phone messages; CAPTCHA on sign-up if abuse appears |
| Account enumeration through sign-up, login or codes | Uniform responses: "if the number is valid, a code was sent"; one generic login error (V1 behaviour) |
| Applicant files (CV) used to attack reviewers | Type and size checks, virus scan before any reviewer can open the file, served as `attachment`; downloads audited |
| Insider fraud (refunds, discounts, voids) | Four-eyes approvals; requester ≠ approver; immutable cash sessions; daily close report; audit |
| Leaked logs | PII and clinical masking in Serilog; no bodies on sensitive endpoints; no tokens (V1 C3, M8 fixed) |
| Malicious uploads | Type and size checks, virus scanning, served with `Content-Disposition: attachment`, never executed |
| Injection in documents and messages | HTML encoding in emails (V1 M10); PDF renderer escapes text; templates use placeholders only |
| Webhook forgery | Paymob HMAC verification, idempotency on the transaction id, amount and merchant-order checks against the stored payment intention |
| Lost or stolen device at a branch | Short access tokens (15 min); idle timeout per role; display devices use limited device tokens |
| Ransomware / data loss | Backups with off-site copies and restore tests ([04 NFR-04](04_FUNCTIONAL_REQUIREMENTS.md#5-non-functional-requirements)); least-privilege database login (`clinicflow_app`, already in V1's compose setup, NM6) |
| Prompt injection or data leaks through the chatbot (P7) | The bot only calls public V2 services and the patient's own booking endpoint; no clinical data in prompts; output never executed; per-user rate limit and cost cap |

## 3. Data protection

- **In transit:** TLS 1.2+ for all traffic, including the database connection (`Encrypt=True`, a valid certificate in production; no `TrustServerCertificate=True` outside Development) and SMTP, WhatsApp and SMS providers.
- **At rest:**
  - SQL Server Transparent Data Encryption (TDE) for the whole database (or the managed equivalent).
  - Column-level encryption (ASP.NET Data Protection with a key in a secret store, or SQL Always Encrypted) for C1 free text: encounter notes, addenda, prescription instructions. Searchable fields (diagnosis codes) stay unencrypted.
  - File storage encrypted by the provider; attachments are never public.
- **Keys and secrets:** in a secret store (Azure Key Vault or equivalent), never in `appsettings.json` (V1 rule kept). The JWT key, data-protection keys and provider keys are rotated at least yearly and on staff departures with access.
- **Database access:** the application already uses a least-privilege login, `clinicflow_app`, created by `docker/db-init.sql` (no `sa`; V1). V2 adds a separate deploy login for migrations (once they leave startup) and denies the app login `UPDATE`/`DELETE` on `AuditEntry`.

## 4. Access control and audit

- **Least privilege by default:** the matrix in [02 §6](02_ROLES_AND_PERMISSIONS.md#6-role--permission-matrix). `Admin` has no clinical access.
- **Treating relationship:** a doctor can read a patient's encounters when they have an appointment with that patient (past 12 months or upcoming), or, with `clinical.shareWithinBranch` on, when the patient is in their branch queue today. Otherwise they need break-glass.
- **Break-glass:** reason required (dropdown + free text); 60-minute grant; banner shown while active; audit entry flagged; daily digest to the medical director.
- **Audit:** every write on C1–C3 and access-control records, every read of C1 (FR-AUD-01/02). Entries include user, staff id, IP, time, entity, patient, before/after (C1 values are not copied into audit, only "changed" markers and the entity version), reason.
- **Access reviews:** quarterly, each branch manager confirms their staff's roles; the Admin removes stale accounts. A report lists accounts unused for 60 days.
- **Sessions:** staff idle timeout 15 minutes on shared front-desk machines, 60 minutes elsewhere (setting); refresh tokens 12 hours for staff (vs 7 days for patients).

## 5. Privacy and consent

Egypt's **Personal Data Protection Law No. 151 of 2020** treats health data as **sensitive personal data**. The checklist below is what the plan assumes; each point is **(confirm)**:

| # | Obligation (as understood) | How V2 supports it | Owner |
|---|---|---|---|
| P1 | A legal basis and **explicit consent** for processing sensitive (health) data | `Consent` records (type, version, timestamp, channel); booking blocked until current consent is accepted (FR-PAT-08); desk registration captures consent on a signed form, recorded in the system | Clinic + product |
| P2 | **Licence / registration** with the Personal Data Protection Centre for processing sensitive data **(confirm)** | Not a software feature; tracked as a go-live blocker | Clinic legal |
| P3 | A **data protection officer** **(confirm)** | Named in organization settings; receives breach alerts and access-review digests | Clinic |
| P4 | Data subject rights: access, correction, objection, deletion where allowed | Patient portal shows the data held; correction requests go to the desk; export of a patient's data as PDF/JSON by `patients.edit` + audit | Product |
| P5 | **Breach notification** to the Centre within the legal deadline (72 hours in the law's text) and to affected people **(confirm)** | Incident runbook; audit and access logs to scope a breach; contact list of affected patients | Clinic + IT |
| P6 | **Cross-border transfer** restrictions **(confirm)** | Hosting region is a go-live decision: in Egypt, or abroad only if permitted. Providers (WhatsApp, SMS, email, storage) reviewed for data location | Clinic + IT |
| P7 | Purpose limitation and minimisation | Marketing is out of scope; notifications carry no clinical details ("You have an appointment at …", never the diagnosis) | Product |
| P8 | Processor agreements with vendors | Contracts with hosting, WhatsApp (Meta or a BSP), SMS, Paymob, email and (P7) LLM providers | Clinic |
| P9 | Applicants' personal data (CVs) is collected for hiring only **(confirm)** | Applicants accept a recruitment privacy notice when applying; files of unsuccessful applicants are deleted after the retention period (FR-REC-08) | Clinic + product |

Additional medical-confidentiality and record-keeping rules (Egyptian medical syndicate ethics, Ministry of Health requirements for clinics) also apply **(confirm)**; they mainly affect retention (§6) and who may access records.

## 6. Retention and deletion

| Data | Retention (planning default, configurable) | Notes |
|---|---|---|
| Clinical records | Not deleted while the organization operates; minimum period **(confirm)** | Deletion requests for clinical data are generally refused where law requires keeping records; the refusal is recorded |
| Financial records (invoices, payments, claims) | At least the period required by tax law **(confirm; commonly 5+ years)** | Immutable |
| Audit entries | 7 years | Append-only; archived to cold storage after 1 year |
| Message delivery logs | 1 year | Phone numbers masked after 90 days |
| Application logs | 90 days | No C1; masked C2 |
| Inactive patient accounts (no visits) | Login data deleted on request; the patient record is kept if there was any visit | |
| Staff accounts after departure | Deactivated, not deleted (authorship of records) | |
| Attachments | As clinical records | |
| Doctor application files (rejected or withdrawn) | 6 months, then the files are deleted; the application row is kept with a marker **(confirm)** | FR-REC-08. Accepted applicants' files move to the staff file |
| Phone verification codes | 1 day | Hashed; purged hourly |

Deletion in V2 means **anonymisation** where the record must remain for others (e.g. an invoice keeps amounts but the patient's identifying fields are replaced), done by a controlled job with an audit entry.

## 7. Payments (PCI DSS)

- Paymob's hosted checkout (cards and wallets) → ClinicFlow is out of scope for card data (SAQ A level **(confirm with Paymob)**).
- No card numbers in logs, notes or attachments (a validator rejects card-number patterns in free-text payment references).
- Card-present POS terminals at the desk are standalone; the receptionist records the terminal's approval code.

## 8. Secure development

- Threat modelling for each phase at design time (this document is the baseline).
- Security tests in CI: anonymous-endpoint allowlist test, permission matrix tests, dependency vulnerability scan (V1 practice), secret scanning.
- An external **penetration test** before go-live of P3 (payments) and P4 (EMR).
- REVIEW-style code review per phase, recorded in `docs/REVIEW.md` or a V2 equivalent.

## 9. Incident response

1. Detect: alerts on login anomalies, break-glass spikes, webhook failures, unusual exports, phone-message volume spikes.
2. Contain: deactivate accounts, revoke all sessions (V1 `RevokeAllActiveAsync`), rotate keys.
3. Scope: audit and access logs answer "who saw what".
4. Notify: DPO → legal → the Centre and patients within the deadline (P5).
5. Review: a blameless post-incident report and REVIEW entries for the fixes.
