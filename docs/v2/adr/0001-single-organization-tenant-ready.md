# ADR 0001: One organization in V2, with a tenant-ready schema

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-09 |
| **Deciders** | Product owner, backend lead |

## Context

V2 serves one clinic organization with several branches. The business expects a later version (V3) to host many clinic organizations on one platform, a SaaS model where each clinic has its own staff, patients' records, prices and money.

Retrofitting multi-tenancy later is expensive. Every table, query, unique index, cache key, background job and report must learn about the tenant, and any query that forgets leaks one clinic's data to another. Building a full multi-tenant product now is also expensive: onboarding, a platform operator, tenant isolation tests and per-tenant configuration UI, none of which V2 needs.

## Decision

1. Model `Organization` as an entity now, with **exactly one row** in V2.
2. Every **organization-owned** entity has a non-null `OrganizationId`: branches, staff, services, prices, schedules, slots, appointments, patient records, clinical data, invoices, payments, claims, settings, audit entries.
3. **Global** entities stay global: `ApplicationUser`, `PatientProfile` (identity), reference data (ICD-10, specialties).
4. A patient's organization-specific data lives in a `PatientRecord(OrganizationId, PatientProfileId, Mrn)`, so one person can later be a patient of several clinics.
5. A staff member is `StaffMember(OrganizationId, UserId)`, so one person can later work for several clinics.
6. `ITenantContext` provides the current organization. EF Core **global query filters** apply it to every organization-owned entity, and a SaveChanges interceptor stamps and checks it.
7. Unique indexes include `OrganizationId`. Background jobs iterate organizations.
8. **Not built in V2:** organization onboarding, a platform-operator role, per-organization branding UI, tenant resolution by host name, cross-organization search.

## Consequences

- Positive: V3 adds organizations without schema changes to existing tables, and isolation is enforced from day one by filters and tests (a second organization is seeded in integration tests).
- Positive: the same design gives a clean place for organization settings (cancellation windows, fees, limits) that V1 hardcodes.
- Negative: an extra column and index on most tables, and a filter on every query. The cost is small at V2 scale.
- Negative: developers must use `IgnoreQueryFilters()` deliberately (only in platform jobs and migrations). A code review rule and an analyzer test guard this.
- Risk: a raw SQL query or `ExecuteUpdate` that bypasses filters. Mitigation: repositories add the organization predicate explicitly in raw and bulk operations, and tests cover them.
