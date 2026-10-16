# ADR 0005: V2 endpoints live under `/api/v2`; V1 routes are frozen, then removed

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-09 |
| **Deciders** | Backend lead, frontend lead |

## Context

The API is a contract with a separate frontend repository ([API_CONTRACT.md](../../API_CONTRACT.md)). V1 has no version segment in its URLs, and its contract says breaking changes need a major version bump and an agreed date (§11).

V2 changes many shapes at once: paged lists, ProblemDetails everywhere, corrected status codes, multiple roles per user, new statuses, prices per service, encounters instead of doctor notes. Changing V1 routes in place would force the frontend to switch every screen on the same day. The frontend will instead migrate screen by screen over several phases.

## Decision

1. All V2 endpoints are under **`/api/v2/...`**, following the conventions in [07 §3](../07_API_DESIGN.md#3-conventions).
2. The unversioned `/api/...` routes are **V1**. From the start of P0 they are frozen: only bug and security fixes, recorded in the 1.x changelog.
3. V1 and V2 share services and data. Where V1 behaviour still matches, V1 controllers call V2 services, so there is one implementation.
4. A V1 endpoint is deprecated when the frontend no longer uses it: it returns `Deprecation` and `Sunset` headers, and the date goes into `API_CONTRACT.md` §9.
5. V1 endpoints whose concept disappears (doctor self-registration, doctor approval and suspension) are removed at the end of P1. All remaining V1 endpoints are removed at V2 general availability ([10 §2](../10_ROADMAP.md#2-phases)).
6. The contract becomes 2.0.0 with the first `/api/v2` endpoint. Each phase adds to it (2.x) and regenerates `openapi.json` with both versions until V1 is removed, then V2 only.
7. No `Asp.Versioning` package is needed: the version is part of the route templates. A shared route prefix constant keeps it consistent.

## Consequences

- Positive: the frontend migrates one screen at a time, and the old and new contracts are both explicit and testable.
- Positive: V1's known quirks (bare arrays, missing `Z`, 500s) are fixed in V2 without surprising V1 clients.
- Negative: two sets of controllers for a while, and the OpenAPI document is larger. Mitigated by thin V1 adapters and a firm removal date.
- Negative: tests must cover V1 until it is removed. The existing V1 integration tests stay until then.
