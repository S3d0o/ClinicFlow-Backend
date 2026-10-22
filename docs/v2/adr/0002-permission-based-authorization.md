# ADR 0002: Permission-based, branch-scoped authorization

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-09 |
| **Deciders** | Product owner, backend lead, frontend lead |

## Context

V1 authorizes with role names: `[Authorize(Roles = "Admin")]` on controllers, and `if (role == "Patient")` branches in controllers and services. REVIEW.md counts about 24 role literals (§4.7), found a controller that dispatches by the first role claim (NL10), and found endpoints that became public when an attribute was commented out (C1, NM5).

V2 has seven roles. Staff can hold several roles, and most roles act only within the branches they are assigned to. Clinics also differ in details: in one clinic the accountant approves refunds, in another only the manager does. Role checks would multiply into combinations such as "Manager or Accountant, at this branch, below this amount".

## Decision

1. Authorization checks **permissions** (`appointments.check_in`, `refunds.approve`, …), never role names. Roles are bundles of permissions, defined in a default matrix ([02 §6](../02_ROLES_AND_PERMISSIONS.md#6-role--permission-matrix)) and stored in `RolePermission` per organization.
2. Each grant has a **scope**: `Own`, `Branch` (the user's assigned branches) or `Organization`.
3. Two layers:
   - **Endpoint:** `[HasPermission("...")]` rejects users who have the permission in no scope (403).
   - **Resource:** services call `IAccessGuard.EnsureAsync(permission, record.BranchId / ownerId)` and filter lists by `AllowedBranchIds(permission)`. This generalises V1's ownership checks.
4. A **fallback policy** requires authentication everywhere; public endpoints opt out with `[AllowAnonymous]`, verified by an allowlist test. (V1 already has the fallback policy and a test that every action declares one or the other; V2 adds the allowlist.)
5. Tokens carry identity, organization, staff id and role names, **not** permissions. Permissions are resolved on the server and cached; changes apply at the next refresh (≤ 15 min).
6. Hard rules in code that configuration can't override: no clinical permissions for `Admin` or `Accountant`; approvals require requester ≠ approver.
7. `GET /api/v2/me` returns the effective permissions with their scopes, and the frontend builds menus from them, never from role names.
8. New responsibilities become **permissions, not roles**. For example, reviewing doctor applications is `recruitment.view` / `recruitment.manage`, which Managers hold by default and the Admin can grant to any role (2026-10-22). There is no `HR` role.

## Consequences

- Positive: one mechanism for all seven roles and their combinations; clinics can adjust the matrix without code changes; menus and backend checks use the same source.
- Positive: a matrix-driven integration test can check every role × permission × scope combination automatically.
- Negative: more moving parts than role attributes (policy provider, guard, cache). They are small and central.
- Negative: a permission change takes up to 15 minutes to apply. Deactivation is immediate because it revokes refresh tokens, and the remaining access-token lifetime is accepted as in V1.
- V1 endpoints keep their role attributes until they are removed ([ADR 0005](0005-api-v2-versioning.md)).
