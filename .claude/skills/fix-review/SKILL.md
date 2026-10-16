---
name: fix-review
description: Fix one item from docs/REVIEW.md by its ID (e.g. H6, M3, L6, NM5), add tests that prove the fix, and update the item's status in REVIEW.md.
argument-hint: <review item ID, e.g. H6>
disable-model-invocation: true
---

# Fix REVIEW item $ARGUMENTS

## 1. Read the item

Find `$ARGUMENTS` in `docs/REVIEW.md`. It is either a `#### $ARGUMENTS.` heading or a table row starting with `| $ARGUMENTS |`. Read:
- its **Status**, **Where**, **Impact** and **Fix risk**
- any proposed fix

Also read the "Left open on purpose" list near the top.

The file references are from 2026-10-09 and lines may have moved. Open each referenced file and confirm the problem still exists before changing anything. If it is already fixed, say so, offer to update the status, and stop.

## 2. Check for blockers

Stop and ask me before coding if any of these apply:
- **Product decision needed** (the status says so; currently H4, H8, M1, M2, M14). Check `docs/v2/adr/` and `docs/v2/` first, because a decision may already be recorded there. If one is, quote it and propose the fix based on it.
- **API change the frontend would see** (listed in §9 of `docs/API_CONTRACT.md`, or the fix changes a route, field, status code, error code or auth rule). Proceed only through the `/contract-change` process: classify, version bump, update the contract and `openapi.json`.
- **Fix risk High** (migration, many call sites). Show the plan first.

## 3. Fix it

- Make the smallest change that resolves the item, following CLAUDE.md conventions.
- The proposed fix in REVIEW.md is a starting point, not gospel; H5 showed a suggested fix that didn't work. Verify it.
- Write a test that fails before the fix and passes after:
  - service logic: a unit test in `ClinicFlow.Tests/Services/<Service>Tests/`
  - HTTP behaviour: an integration test in `ClinicFlow.IntegrationTests/Tests/<Feature>/`
- Test names follow `Method_Scenario_ExpectedResult`.

## 4. Verify

- `dotnet build ClinicFlow.slnx --no-incremental`: 0 warnings.
- `dotnet test ClinicFlow.Tests`
- `dotnet test ClinicFlow.IntegrationTests` (needs Docker; if Docker isn't running, say so rather than skipping silently).

## 5. Update REVIEW.md

- Set the item's status:
  - `✅ Fixed in <commit>` if fully fixed.
  - `🟡 Partly fixed in <commit>` if partly fixed, saying what remains.
  - Use `<pending commit>` if nothing is committed yet. Mention the tests that cover it.
- Update the **Totals** and **Left open on purpose** lists at the top.
- If tests were added, update the test-coverage status paragraph.

Report what changed, the test results, and whether the frontend is affected. Do not commit unless I ask.
