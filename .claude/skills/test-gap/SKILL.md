---
name: test-gap
description: Write unit tests (and, where HTTP behaviour matters, integration tests) for an untested or under-tested ClinicFlow service, following the project's TestsBase + one-class-per-method pattern. With no argument, list the current coverage gaps.
argument-hint: "[ServiceName, e.g. AdminService]"
---

# Test gap: $ARGUMENTS

## No argument: report the gaps

List every class in `Services/Implementations/` and every public method on its interface in `Services.Abstraction/Contracts/`. Compare them with `ClinicFlow.Tests/Services/*Tests/` (one class per tested method) and `ClinicFlow.IntegrationTests/Tests/`. Also check:
- `ClinicFlow/BackgroundJobs/AppointmentReminderJob.cs`
- the validators in `Presentation/Validators/`

Report a table of service → tested methods / untested methods, ranked by risk. Rank auth, money, ownership checks and state changes first, and trivial getters last. Then stop.

## With a service name: write the tests

1. Read the service, its interface, its `<Feature>Errors`, and its mapping profile.
2. Create `ClinicFlow.Tests/Services/$ARGUMENTSTests/$ARGUMENTSTestsBase.cs`. Copy the pattern of `AppointmentServiceTestsBase`: an `NSubstitute` substitute for every constructor dependency, plus `_sut`.
   - `UserManager<ApplicationUser>` can't be substituted from its interface. Substitute it with `Substitute.For<UserManager<ApplicationUser>>(Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null)`.
   - If a mapping matters to the behaviour under test, use the real AutoMapper configuration, as `UpdateScheduleAsyncTests` does, rather than a substitute.
3. Write one class per public method (`<Method>AsyncTests`). Cover:
   - the happy path, including what was written: assert the `uow` and repo calls with `Received()`
   - each error the method can return, asserting the exact `Error` (code and type)
   - ownership and role checks
   - time rules, using the stubbed `IClock`
   - side effects that must not fail the main operation: make notifications or email throw and assert the result is still success
4. Test names follow `Method_Scenario_ExpectedResult`.
5. If the behaviour depends on the database, write an integration test instead of over-mocking. Examples: conditional `ExecuteUpdate`, unique indexes, transactions. Use the `ClinicFlowApiFactory` + `SeedDataHelper` pattern with `[Collection(nameof(IntegrationTestCollection))]` and `IAsyncLifetime`.

## Verify

- Run `dotnet test ClinicFlow.Tests --filter "FullyQualifiedName~$ARGUMENTSTests"`, then the full unit suite.
- A test that fails because the **code** is wrong is a finding, not something to "fix" in the test. Report it, and check whether it is already in `docs/REVIEW.md`.
- If tests were added for an item REVIEW.md lists as untested, offer to update that status.
