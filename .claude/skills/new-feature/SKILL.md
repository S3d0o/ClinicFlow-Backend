---
name: new-feature
description: Scaffold a new feature end to end (entity, EF config, migration, repository, unit-of-work property, service, DTOs, mapping, validator, errors, controller, tests) following the ClinicFlow layer conventions.
argument-hint: <Entity or feature name, plus a short description of what it does>
disable-model-invocation: true
---

# New feature: $ARGUMENTS

## 0. Create the feature branch first

Before any file changes, run `git status --short`.
- **If the tree is dirty:** stop and ask whether to commit, stash or carry the changes over.
- **If it's clean:** run `git switch master && git pull && git switch -c feature/<kebab-case-name>`, naming the branch after the feature (e.g. `feature/doctor-leave`). Confirm the branch name in your first message.

## 0b. Agree on the shape

Before writing files, show me a short plan and wait for my OK. The plan covers:
- the entity and its fields (types, required, max lengths, indexes), and its relations to existing entities
- endpoints: method, route, role, request, response and status codes
- the error codes, as `Feature.Reason` with their `ErrorType`
- which existing files change

Look at the closest existing feature as a template. `Review` is small and complete: entity, config, repo, service, DTOs, profile, validator, errors, controller, tests. Also check `docs/v2/05_DOMAIN_MODEL.md` in case this entity is already designed there; if it is, follow that design.

## 1. Build it in the order of the CLAUDE.md checklist

1. **Entity:**
   - put it in `Domain/Entities/AppModule/`
   - add a `DbSet` to `Persistance/AppData/ClinicDbContext.cs`
   - add `<Entity>Config` in `Persistance/AppData/Configurations/`
2. **Repository:**
   - `I<Name>Repo` in `Domain/Interfaces/IRepositories/`, `<Name>Repo(ClinicDbContext context)` in `Persistance/Repositories/`
   - add the property to `IUnitOfWork` and the lazy `_x ??= new XRepo(_context)` property to `UnitOfWork`
   - no DI registration for repositories
3. **DTOs:** `*Request` / `*Response` records in `Shared/DTOs/<Feature>/`, plus `*FilterRequest` → `Domain/Parameters/*FilterParams` for lists.
4. **Errors:** `Shared/Errors/<Feature>Errors.cs`. Pick the `ErrorType` by the status it produces. `Failure` is only for unexpected errors.
5. **Service:**
   - the interface in `Services.Abstraction/Contracts/`, the implementation in `Services/Implementations/`
   - inject `IUnitOfWork`, never the DbContext
   - register it in `Services/Extensions/CoreServiceExtensions.cs`
6. **Mapping:** `<Feature>Profile` in `Services/MappingProfiles/`.
7. **Validator:** `<Request>Validator` in `Presentation/Validators/`.
8. **Controller:**
   - put it in `Presentation/Controllers/`; it inherits `ApiController` and has an explicit `[Route("api/...")]`
   - every action has explicit `[Authorize]`, a summary, `[ProducesResponseType]` per status, `ct` as the last parameter and a `HandleResult` body
   - check ownership in the service for any record-specific action
9. **Migration:** `/migration Add<Feature>`, or the CLAUDE.md command with `--output-dir AppData/Migrations`.

Watch the namespace gotchas in CLAUDE.md, and check each project's `Global.cs` before adding usings.

## 2. Tests

- **Unit:** `ClinicFlow.Tests/Services/<Service>Tests/` with a `<Service>TestsBase` (copy the pattern of `AppointmentBookingServiceTestsBase`) and one class per public method. Cover the happy path, not found, the ownership check (403) and each conflict.
- **Integration:** `ClinicFlow.IntegrationTests/Tests/<Feature>/`, following `CancelAppointmentByPatientTests`. Cover each endpoint's success status, anonymous → 401, wrong role → 403 and a validation 400.

## 3. Finish

- `dotnet build ClinicFlow.slnx --no-incremental`: 0 warnings. Then run both test projects; integration tests need Docker.
- New endpoints are a contract change. Finish with the `/contract-change` steps 3 to 5: update the contract docs, regenerate `openapi.json` and bump the minor version.

Do not commit unless I ask.
