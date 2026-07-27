# Development: Remove hidden `DateTime.UtcNow` from `RecurringJobConfiguration`

## Summary

Implemented plan-01.md / design-01.md / architecture-01.md exactly as specified: the `RecurringJobConfiguration` domain entity no longer calls `DateTime.UtcNow` internally. All five call sites (constructor + `Enable`, `Disable`, `UpdateCronExpression`, `UpdateConfiguration`) now take an explicit `DateTime` timestamp parameter supplied by the caller. The two write-side handlers (`UpdateRecurringJobStatusHandler`, `UpdateRecurringJobCronHandler`) now inject `TimeProvider` — mirroring the existing pattern already used by `GetRecurringJobHandler`/`GetRecurringJobsListHandler` — and compute `now` once per `Handle` call. `RecurringJobSeeder` passes `DateTime.UtcNow` inline at its two call sites (startup code, no `TimeProvider` injection per the finding's own guidance).

The concrete, verifiable payoff: `UpdateRecurringJobStatusHandlerTests.cs`'s `LastModifiedAt` assertion was tightened from `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))` to an exact `Should().Be(FixedUtcNow.UtcDateTime)`, using a `Mock<TimeProvider>` fixed to a constant `DateTimeOffset` — the same test-double convention already established in `GetRecurringJobHandlerTests.cs`/`GetRecurringJobsListHandlerTests.cs`. No new NuGet packages were added.

## Files changed

**Domain entity**
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — constructor and all four mutation methods (`Enable`, `Disable`, `UpdateCronExpression`, `UpdateConfiguration`) gained a trailing `DateTime`/`lastModifiedAt`/`modifiedAt` parameter; all five internal `DateTime.UtcNow` reads removed. Validation logic and property semantics unchanged.

**Application layer (production call sites)**
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — passes `DateTime.UtcNow` inline at both the constructor call and the `UpdateConfiguration` call.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobStatus/UpdateRecurringJobStatusHandler.cs` — added `TimeProvider timeProvider` constructor parameter (with null guard), computes `var now = _timeProvider.GetUtcNow().UtcDateTime;` once in `Handle`, passes it to `Enable`/`Disable`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs` — same pattern: `TimeProvider` added as a constructor parameter, `now` computed once, passed to `UpdateCronExpression`.

**Tests (all call sites updated to compile against the new signatures)**
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — entity-level unit tests; all constructor/`Enable`/`Disable`/`UpdateConfiguration` calls pass a literal `DateTime.UtcNow`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobStatusHandlerTests.cs` — added `Mock<TimeProvider>` fixed to a constant `DateTimeOffset` (`FixedUtcNow`), passed into the handler constructor; **tightened** the `LastModifiedAt` assertion in `Handle_Should_Enable_Job_When_IsEnabled_Is_True` from a 5-second `BeCloseTo` tolerance to exact equality.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs` — added the same `Mock<TimeProvider>` pattern (no pre-existing `LastModifiedAt`/`BeCloseTo` assertion existed here to tighten).

## Out of scope (unchanged, per plan)

- `RecurringJobsControllerTests.cs` — builds DTOs directly, never touches the entity.
- No DI registration changes — `TimeProvider.System` is already registered as a singleton in `ServiceCollectionExtensions.cs:130`.
- No HTTP/API contract, database schema, or `IRecurringJobConfigurationRepository` changes.

## Verification performed

`dotnet` is not available in this sandbox (no SDK on `PATH`, none found via filesystem search), so `dotnet build`/`dotnet format`/`dotnet test` could not be executed directly here. In its place I did an exhaustive manual verification:

1. Re-grepped the entire `backend/` tree for every call site of `new RecurringJobConfiguration(`, `.Enable(`, `.Disable(`, `.UpdateCronExpression(`, `.UpdateConfiguration(` — confirmed the same 12 files identified in architecture-01.md's inventory were touched, no others.
2. Wrote a small Python balanced-paren parser to extract every one of those call expressions from all 13 changed files and count top-level arguments — confirmed every call now supplies the new trailing timestamp argument (constructor: 8 args, `Enable`/`Disable`: 2 args, `UpdateCronExpression`: 3 args, `UpdateConfiguration`: 6 args). No mismatches found.
3. Verified brace/paren counts balance in every changed file (no truncated edits).
4. Read every changed file in full to confirm signatures, parameter ordering, and body logic match design-01.md exactly (`modifiedBy` first, `modifiedAt`/`lastModifiedAt` trailing; `now` computed exactly once per `Handle`).
5. Confirmed `TimeProvider.System` DI registration exists and no other production code manually constructs `UpdateRecurringJobStatusHandler`/`UpdateRecurringJobCronHandler` with the old 3-/4-arg constructor.

**Recommended follow-up verification** (once `dotnet` SDK is available in this environment or in CI): run `dotnet build` then `dotnet test --filter "FullyQualifiedName~BackgroundJobs"` to execute the full BackgroundJobs test suite, and `dotnet format` to confirm style compliance. Given the mechanical nature of this refactor (compiler-enforced signature change, no behavior change beyond timestamp precision) and the manual verification above, I'm confident the build will succeed, but an actual `dotnet build`/`test` run should still be done before merge as the project's standard validation gate.
