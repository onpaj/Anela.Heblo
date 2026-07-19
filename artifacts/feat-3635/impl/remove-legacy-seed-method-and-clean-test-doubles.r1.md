# Implementation: remove-legacy-seed-method-and-clean-test-doubles

## What was implemented

Removed `SeedDefaultConfigurationsAsync` from `IRecurringJobConfigurationRepository` and its EF Core implementation (`RecurringJobConfigurationRepository`), now that nothing calls it — the startup call site was switched to `IRecurringJobSeeder` in the previous task. Removed the two now-redundant seeding tests (and their `CreateMockJobs`/`MockRecurringJob` helpers) from `RecurringJobConfigurationRepositoryTests.cs`, since that coverage now lives in `RecurringJobSeederTests.cs` (added in task 1). Removed the stale `SeedDefaultConfigurationsAsync` stub overrides from the manual test doubles in `HangfireRecurringJobSchedulerTests.cs` and `RecurringJobDiscoveryServiceTests.cs` (two stub classes in the latter). Note: the `AddAsync` no-op stubs for these test doubles were already added in task 1 (a necessary side effect of adding `AddAsync` to the interface early) — this task only needed to drop the obsolete `SeedDefaultConfigurationsAsync` overrides, not add `AddAsync` again.

This completes the interface narrowing: `IRecurringJobConfigurationRepository` is now pure CRUD (`GetAllAsync`, `GetByJobNameAsync`, `AddAsync`, `UpdateAsync`), matching spec FR-2/FR-5.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs` — removed `SeedDefaultConfigurationsAsync` from the interface.
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — removed the `SeedDefaultConfigurationsAsync` implementation (30 lines).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs` — removed `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`, `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`, and the `CreateMockJobs`/`MockRecurringJob` helpers that existed only to support them (99 lines removed).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — removed the stale `SeedDefaultConfigurationsAsync` override from `EmptyStubRepository` (the `AddAsync` no-op was already present from task 1).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs` — removed the stale `SeedDefaultConfigurationsAsync` override from both `StubRecurringJobConfigurationRepository` and `StubDbRecurringJobConfigurationRepository`.

## Tests

- Seeding-behavior coverage (empty-seed and no-duplicate cases) now lives solely in `RecurringJobSeederTests.cs` (added in task 1), targeting `IRecurringJobSeeder`/`RecurringJobSeeder` instead of the repository.
- No test double of `IRecurringJobConfigurationRepository` implements a seeding method anymore.
- Full `Anela.Heblo.Tests.Features.BackgroundJobs` suite: 90 tests (92 from task 1, minus the 2 removed seed tests here), all passing.

## How to verify

```bash
cd /home/user/worktrees/feature-3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
dotnet format Anela.Heblo.sln --verify-no-changes
grep -rn "SeedDefaultConfigurationsAsync" backend/ --include="*.cs"
```

Actual results observed in this session:
- `dotnet build Anela.Heblo.sln`: **Build succeeded**, 0 errors (same pre-existing, unrelated `AccessMatrixGen` post-build warning as prior tasks, `ContinueOnError="true"`).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"`: **Passed! - Failed: 0, Passed: 90, Skipped: 0, Total: 90**.
- `dotnet format Anela.Heblo.sln --verify-no-changes`: exit code 0, no formatting differences.
- Grep confirms every remaining `SeedDefaultConfigurationsAsync` reference is on `IRecurringJobSeeder`/`RecurringJobSeeder`/`RecurringJobSeederTests` and the startup call site (`seeder.SeedDefaultConfigurationsAsync(...)`) — none on `IRecurringJobConfigurationRepository` or its implementation.

## Notes

- The assigned developer subagent stalled waiting on a background test/build process it never re-polled (same pattern seen in tasks 1 and 2 in this pipeline run). The orchestrator verified the subagent's applied diff was correct, then ran build/test/format checks and committed directly.
- Commit: `76b94d4` on branch `feature/3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio`, message `#3635: Remove SeedDefaultConfigurationsAsync from IRecurringJobConfigurationRepository and clean up test doubles`.
- This is the final task in the plan — `IRecurringJobConfigurationRepository` is now CRUD-only, matching the original issue's suggested fix.

## Status
DONE
