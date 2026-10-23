---
name: conventions-check
description: Check the current diff (or a given branch/commit range) against ClinicFlow's house rules from CLAUDE.md, such as Result pattern, IClock, structured logging, AsNoTracking, CancellationToken flow, swallowed side effects and contract-doc updates. Use before committing or opening a PR.
argument-hint: "[git range, default: uncommitted changes + branch vs master]"
---

# Conventions check $ARGUMENTS

## Scope

- With an argument, check `git diff $ARGUMENTS`.
- Without one, check `git diff master...HEAD` plus `git diff HEAD` (uncommitted).

Only report problems in **changed lines**, and their immediate context. Pre-existing issues are out of scope unless the change makes them worse.

## Rules to check

**Services**
- No `throw` for expected failures. Return an `Error` from `<Feature>Errors`, which converts implicitly to `Result`.
- A `Result` must not mix `Validation` errors with other types; that throws.
- `ErrorType` matches the intended status. Flag new `Error.Failure` used for an expected case (M3).
- New errors are declared in `Shared/Errors/<Feature>Errors.cs` with a `Feature.Reason` code, not inline.
- No `DateTime.Now` / `DateTime.UtcNow` / `DateTime.Today` / `DateTimeOffset.Now` / `TimeProvider.System` in production code (`ClockUsageTests` enforces it). Use the injected `IClock`, and set entity timestamps from it: entities have no `DateTime.UtcNow` defaults.
- Services inject `IUnitOfWork`, never `ClinicDbContext`.
- A warning is logged before returning an expected failure.
- Notifications and emails after the main write go through a try/log/swallow helper (`TryNotifyAsync` style). Emails for an already-committed change don't receive the request `ct`.

**Logging**
- Structured templates with named placeholders. Flag `$"..."` or string concatenation inside `logger.Log*` calls.

**Async and data access**
- Async methods end in `Async` and take `CancellationToken ct` as the **last** parameter.
- `ct` is passed to every EF call.
- No `.Result`, `.Wait()` or `async void`.
- Read-only queries use `AsNoTracking()`.
- Bulk updates use `ExecuteUpdateAsync` / `ExecuteDeleteAsync`.
- Multi-step writes use `uow.ExecuteInTransactionAsync(async tx => { ...; await tx.CommitAsync(ct); ... }, ct)`, with the commit as the operation's last step. Flag emails, notifications or other side effects inside the operation: a retry would repeat them.
- No N+1: no queries inside loops over query results.

**Wiring**
- A new service is registered in `CoreServiceExtensions`.
- A new repo is exposed on `IUnitOfWork` / `UnitOfWork` and **not** registered in DI.
- A new background job is removed in `ClinicFlowApiFactory`.
- No usings that duplicate the project's `Global.cs`.

**Endpoints and contract**
- New or changed actions follow the `/endpoint-audit` rules; check the changed actions only.
- If the diff touches `Presentation/Controllers/**`, `Shared/DTOs/**`, `Shared/Errors/**`, auth attributes or enum values, then `docs/API_CONTRACT.md` and `docs/openapi.json` must change in the same diff. Flag them as **missing contract update** if they don't.

**Migrations**
- If an entity or `*Config` changed, a migration exists, its name is PascalCase, and the model snapshot changed.

**Tests**
- Changed service behaviour has a matching test change. New test names follow `Method_Scenario_ExpectedResult`.

**Secrets**
- No credentials, connection strings or keys added to `appsettings.json` or committed files.

## Output

Findings grouped as **Must fix** / **Should fix** / **Nit**, each with `file:line` and a one-line fix. If everything passes, say so in one line. Don't change files unless I ask.
