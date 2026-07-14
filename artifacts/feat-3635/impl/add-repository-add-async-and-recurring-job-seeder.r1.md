# Implementation: add-repository-add-async-and-recurring-job-seeder

## What was implemented

Added a narrow `AddAsync` primitive to `IRecurringJobConfigurationRepository` / `RecurringJobConfigurationRepository`, then introduced a new `IRecurringJobSeeder` / `RecurringJobSeeder` pair in the Application layer that uses it to seed default recurring-job configurations. This is purely additive: `SeedDefaultConfigurationsAsync` on the repository is untouched and still works, and all pre-existing BackgroundJobs tests still pass.

Followed TDD per the task context: added a failing test for `AddAsync` (confirmed compile error before implementing), implemented `AddAsync`, added a failing test file for `RecurringJobSeeder` (confirmed compile error before implementing), then implemented `IRecurringJobSeeder`/`RecurringJobSeeder` and confirmed all tests pass.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs` — added `Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)` to the interface.
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — implemented `AddAsync` (stages the entity via `AddAsync` on the DbSet, then calls `SaveChangesAsync` in the same call, matching the self-committing convention already used by `UpdateAsync`).
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs` — new interface, `SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)`.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — new implementation: for each discovered job, checks `GetByJobNameAsync`, and calls `_repository.AddAsync` only if no existing configuration is found.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs` — added `AddAsync_WithNewConfiguration_PersistsAndIsRetrievable` test.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — new test file mirroring the existing repository seed tests, exercising `RecurringJobSeeder` directly.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs` — **not in the original task file list**; added `AddAsync` no-op implementations to the two private test-double classes (`StubRecurringJobConfigurationRepository`, `StubDbRecurringJobConfigurationRepository`) that implement `IRecurringJobConfigurationRepository`. Without this the build fails with `CS0535` once `AddAsync` is added to the interface.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — **not in the original task file list**; same fix for the `EmptyStubRepository` test double (`CS0535`).

## Tests

- `RecurringJobConfigurationRepositoryTests.AddAsync_WithNewConfiguration_PersistsAndIsRetrievable` — verifies a new configuration persists via `AddAsync` and is retrievable via `GetByJobNameAsync`.
- `RecurringJobSeederTests.SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` — verifies all 9 default job configurations are created via the seeder.
- `RecurringJobSeederTests.SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` — verifies the seeder does not duplicate an existing configuration.
- All pre-existing tests in `Anela.Heblo.Tests.Features.BackgroundJobs` (92 total) continue to pass, including the repository, discovery service, and Hangfire scheduler tests whose test doubles were updated.

## How to verify

```bash
cd /home/user/worktrees/feature-3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
```

Actual results observed in this session:
- `dotnet build Anela.Heblo.sln`: **Build succeeded**, 0 errors (13 pre-existing warnings unrelated to this change; the `GenerateAccessMatrix` post-build step in `Anela.Heblo.API.csproj` throws a pre-existing, unrelated JSON parsing exception in `Anela.Heblo.AccessMatrixGen`, but that target has `ContinueOnError="true"` so it does not fail the build — confirmed present before any of my changes too).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"`: **Passed! - Failed: 0, Passed: 92, Skipped: 0, Total: 92** (`Anela.Heblo.Tests.dll`).
- Individually verified: `AddAsync_WithNewConfiguration_PersistsAndIsRetrievable` and both `RecurringJobSeederTests` pass (3/3).
- Before implementation, confirmed compile failures as expected: `CS0246: The type or namespace name 'RecurringJobSeeder' could not be found` (Step 3), and `CS0535` errors on the three test-double classes once `AddAsync` was added to the interface (Step 2 side effect, see Notes).

## Notes

- **Deviation from task file**: the task context's exact commit file list omitted two test-double files (`RecurringJobDiscoveryServiceTests.cs`, `HangfireRecurringJobSchedulerTests.cs`) that implement `IRecurringJobConfigurationRepository` via private stub classes. Adding `AddAsync` to the interface broke their compilation (`CS0535`), contradicting the task's claim that "all existing tests keep passing throughout" without further changes. I added minimal no-op `AddAsync` implementations to these stubs (matching the existing no-op pattern of their other methods) and included both files in the commit. This was necessary for the build to succeed at all.
- **Deviation from task file**: the task's example `IRecurringJobSeeder.cs` omitted `using Anela.Heblo.Domain.Features.BackgroundJobs;`, which is required for the `IRecurringJob` reference to resolve (confirmed every other file in that folder referencing `IRecurringJob` has this using, and there is no global using covering it). Added the using statement so the file compiles.
- The full-solution `dotnet build`/`dotnet test` commands are very slow in this environment (each full-solution invocation takes several minutes to build ~40 projects), but all runs completed successfully with no hangs — confirmed via process-tree inspection during longer runs.
- No changes were made to `SeedDefaultConfigurationsAsync` on the repository; it remains as-is per the task's stated scope (removal is deferred to the `remove-legacy-seed-method-and-clean-test-doubles` follow-up task).

## Status
DONE
