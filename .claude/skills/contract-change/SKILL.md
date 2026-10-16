---
name: contract-change
description: Make a change that affects the API contract shared with the frontend (route, request/response field, status code, error code or auth rule) and update docs/API_CONTRACT.md and docs/openapi.json in the same change, following §11 of the contract.
argument-hint: <description of the API change>
disable-model-invocation: true
---

# Contract change: $ARGUMENTS

The API is a contract shared with the frontend repo. Code, `docs/API_CONTRACT.md` and `docs/openapi.json` change together or not at all.

## 1. Classify the change

Read §9, §10 and §11 of `docs/API_CONTRACT.md`, then decide:

- **Additive** (new endpoint, new optional request field, new response field, new error code): minor bump, `1.x.0`.
- **Breaking** (remove or rename a field or route, change a type, status code, enum value, auth rule or required field): major bump, `x.0.0`.
- **Bug fix that changes observed behaviour**: still goes in the changelog.

If it is breaking and **not already listed in §9**, stop and tell me. §11 says breaking changes are listed in §9 first and agreed with the frontend. Offer to add the §9 row instead of shipping it. If there is a backward-compatible path (add the new form, keep the old one), propose it.

State the classification and the version bump before writing code.

## 2. Make the code change

Follow CLAUDE.md conventions: controller attributes, `HandleResult`, `Result`/`Error` in services, `<Feature>Errors`, a validator in `Presentation/Validators`, explicit `[Authorize]`. Add or update unit and integration tests. Integration tests assert status codes, so update those that the change intentionally affects.

## 3. Update `docs/API_CONTRACT.md`

Update every place the change shows up:
- The header table: **Contract version** and **Last updated** (today's date).
- §4 endpoint index, if a route or its auth changed.
- §5 endpoint section: body rules, success response, errors.
- §3.3 error code catalog: new codes, changed statuses. Remove the *(Mx)* marker once a REVIEW item ships.
- §6 data types and §7 enums, if a DTO or enum changed.
- §12 TypeScript types, if a DTO or enum changed.
- §9: remove the row if this change was pending there.
- §10 changelog: add a row with the version, the date and what the frontend will notice.

## 4. Regenerate `docs/openapi.json`

Run the `/regen-openapi` steps: start the API on the http profile, save `http://localhost:5143/openapi/v1.json` to `docs/openapi.json`, and make sure `servers[0].url` is `http://localhost:5143/`. Check that the diff contains the change you made and nothing unexpected.

## 5. Verify and report

- `dotnet build ClinicFlow.slnx` with 0 warnings.
- `dotnet test ClinicFlow.Tests`. Also run `dotnet test ClinicFlow.IntegrationTests` if Docker is running; if it isn't, say so.

Report:
- the version bump
- the files changed
- a ready-to-paste note for the frontend PR: what changed and what the frontend must do. §11 asks for the same text to be mirrored into the frontend repo's `docs/API_CONTRACT.md`.

Do not commit unless I ask.
