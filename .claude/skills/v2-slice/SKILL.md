---
name: v2-slice
description: Turn a V2 roadmap phase, functional requirement or user journey from docs/v2 into a concrete implementation plan against the current V1 codebase (affected code, entities, migrations, endpoints, tests, open decisions). Planning only, no code changes.
argument-hint: <phase (e.g. P2), FR id, or journey (e.g. J5)>
---

# V2 slice plan: $ARGUMENTS

Planning only: change no code.

## 1. Gather the requirements

1. Find `$ARGUMENTS` in `docs/v2/`:
   - phases are in `10_ROADMAP.md`
   - requirements are in `04_FUNCTIONAL_REQUIREMENTS.md`
   - journeys are in `03_USER_JOURNEYS.md`

   Quote the scope it covers.
2. Pull in what constrains it:
   - `05_DOMAIN_MODEL.md` (entities), `06_TECHNICAL_DESIGN.md`, `07_API_DESIGN.md`
   - `02_ROLES_AND_PERMISSIONS.md` (permissions), `08_SECURITY_PRIVACY_COMPLIANCE.md`
   - `09_MIGRATION_FROM_V1.md`
   - every ADR in `docs/v2/adr/`. These are binding decisions; cite them by number.
3. Check the dependencies in `10_ROADMAP.md`. If an earlier phase isn't done yet, say what's missing.

## 2. Map it onto the code

- Which existing entities, services, controllers and endpoints change, and which are new. Give file paths.
- Which open `docs/REVIEW.md` items this slice resolves or collides with. For example:
  - the state-machine ADR versus the appointment status items
  - the time-zone ADR versus the clinic time-zone item
  - permission-based auth versus role strings in `[Authorize(Roles = ...)]`
- Data: new tables and columns, migration risk, backfill of existing V1 rows, and changes to `DataSeeder`.
- API:
  - new `/v2` routes, per ADR-0005 (API versioning)
  - effects on the V1 contract in `docs/API_CONTRACT.md`
  - what goes into §9 of the contract

## 3. Output

1. **Summary**: what the slice delivers, in 3–5 lines.
2. **Work breakdown**: ordered steps, each small enough for one PR, with the files touched, the tests to add and whether it changes the contract.
3. **Migrations**: list them with their risk.
4. **Open questions**: decisions the docs don't settle, phrased as questions for me.
5. **Risks**, and what can run in parallel.

Offer to save the plan as `docs/v2/plans/$ARGUMENTS.md`.
