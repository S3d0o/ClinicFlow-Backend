# ADR 0007: V2 is a separate product line with its own branch and database; V1 stays untouched

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-10-22 |
| **Deciders** | Owner and only developer |
| **Supersedes** | [ADR 0005](0005-api-v2-versioning.md) decisions 2–5 (V1 frozen inside the V2 app, shared services and data, deprecation, removal at GA) |

## Context

The first plan built V2 on `master`, in the same running app and on the same database as V1. Each phase would extend V1's schema in place (expand → migrate → contract), and V1's endpoints would be removed at V2 general availability.

The owner wants V1 kept **separate and untouched**: V1 is finished (contract 3.1.2, tagged `v1.0.0-rc1`) and will be deployed as it is. V2 is built on its own, and the owner decides later whether, and when, V2 replaces V1. Sharing a branch, an app and a database makes every V2 change a risk to V1.

## Decision

1. **Two product lines in each repo** (backend and frontend):

   | Line | Branch | Database | Contract | Tags |
   |---|---|---|---|---|
   | **V1** | `master` (backend), `main` (frontend) | V1's database (`ClinicFlow`) | 3.x | `v1.0.0` at deployment, `v1.x.y` for fixes |
   | **V2** | `v2` in both repos | its own database (`ClinicFlowV2`) | 4.x | `v2.0.0-p<n>` per phase, `v2.0.0` at V2 GA |

2. **V1 changes only when the owner asks**, for example a fix needed in production. Those fixes are made on `master` (or a `fix/` branch off it) and cherry-picked into `v2` when they also apply there.
3. **V2 work branches off `v2`:** `feature/p<n>-<name>`, `fix/…`, `docs/…`. Each branch is merged back into `v2`. Nothing from V2 is merged into `master` unless decision 7 applies.
4. **V2 has its own database:**
   - its own connection string, compose project, container names and ports
   - migrations on `v2` apply only to it
   - V2 code never migrates or writes to V1's database
   - V2 may reshape the schema freely; the expand → migrate → contract dance against V1's live data is no longer needed
5. **API:** V2 endpoints stay under **`/api/v2`**, so it is always obvious which API a client calls and both can sit behind one domain if needed. [ADR 0005](0005-api-v2-versioning.md) decisions 1, 6 and 7 still apply. The unversioned routes inherited on the `v2` branch:
   - serve only the V2 frontend while its screens are rebuilt
   - are **not** frozen and need no `Deprecation`/`Sunset` headers
   - are deleted from `v2` as soon as the V2 frontend no longer calls them, and at the latest at V2 GA
   - V1's own clients keep using the V1 deployment.
6. **Switching is the owner's decision.** If V2 is chosen, a one-time **import tool** ([09 §2](../09_MIGRATION_FROM_V1.md#2-importing-v1-data)) copies V1's data into the V2 database:
   - it reads V1 read-only
   - it runs a dry run first, on a copy
   - it produces the reconciliation report
   - V1 stays deployable as the fallback until the owner retires it.
7. **If V2 replaces V1:** `v2` is merged into `master` (and `main`) at V2 GA, and V1 stays reachable through its tags and a `release/v1` branch cut from the last V1 tag. Until then, `master` is V1.

## Consequences

- Positive: V1 is safe. No V2 commit, migration or deployment can change it, and it can be deployed and fixed on its own schedule.
- Positive: V2 is free to rename, reshape and drop things without keeping a live V1 database and API working in the meantime.
- Positive: the owner can run both, compare them, and choose.
- Negative: the two lines drift apart. A V1 fix has to be cherry-picked into `v2` (or written again once the code has diverged).
- Negative: moving to V2 needs an import tool and a cut-over (stop V1 writes, import, switch DNS), not a gradual in-place upgrade.
- Negative: running both means two deployments and two databases to host and back up.
