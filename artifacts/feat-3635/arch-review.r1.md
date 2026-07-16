# Architecture Review: Extract Recurring Job Seeding out of `IRecurringJobConfigurationRepository`

## Skip Design: true

Pure backend structural refactor: no controller, DTO, OpenAPI, or React surface changes. Confirmed by inspecting every call site — `IRecurringJobConfigurationRepository` and `SeedDefaultConfigurationsAsync` are consumed only by MediatR handlers, one Hangfire hosted service, and startup wiring; none of it crosses the HTTP boundary.

## Architectural Fit Assessment

The finding is correct and the fix is squarely within established convention. Verified against the actual source:

- `IRecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`) has 4 members today: `GetAllAsync`, `GetByJobNameAsync`, `UpdateAsync`, `SeedDefaultConfigurationsAsync`.
- The implementation (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`) confirms `SeedDefaultConfigurationsAsync` builds `RecurringJobConfiguration` from `IRecurringJob.Metadata`, checks `GetByJobNameAsync` per job, and does a single `_context.SaveChangesAsync()` after the loop (not per-item) — this matters for FR-2's "identical database state" requirement.
- The sole call site is `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync` (line 451-474), invoked from `Program.cs:169` after `app.Build()`. I confirmed `AddApplicationServices` → `AddBackgroundJobsModule` runs at `Program.cs:103`, well before line 169, so `IRecurringJobSeeder` will already be registered in the container when the seeding extension resolves it — FR-3's acceptance criterion holds without any reordering.
- `BackgroundJobsModule.cs` already registers sibling narrow-interface services in the same module (`IRecurringJobStatusChecker` → `RecurringJobStatusChecker`), and the `Services/` subfolder already holds `IHangfireJobEnqueuer`, `IHangfireRecurringJobScheduler`. `IRecurringJobSeeder`/`RecurringJobSeeder` fits this existing shape exactly — no new pattern is introduced.
- Test-double impact is exactly as scoped: only 3 hand-written stubs implement the interface (`RecurringJobConfigurationRepositoryTests` is the real impl under test, not a stub) — `HangfireRecurringJobSchedulerTests.EmptyStubRepository`, `RecurringJobDiscoveryServiceTests.StubRecurringJobConfigurationRepository`, and `RecurringJobDiscoveryServiceTests.StubDbRecurringJobConfigurationRepository` (this is the second "line 222" stub the spec refers to, not a duplicate of the first). Five other test files (`GetRecurringJobHandlerTests`, `GetRecurringJobsListHandlerTests`, `RecurringJobStatusCheckerTests`, `UpdateRecurringJobCronHandlerTests`, `UpdateRecurringJobStatusHandlerTests`) use `Mock<IRecurringJobConfigurationRepository>()` (loose Moq, no `MockBehavior.Strict`), so adding `AddAsync` and removing `SeedDefaultConfigurationsAsync` from the interface is transparent to them — confirmed no strict-mode mocks exist for this interface anywhere in the suite.

This is a clean, low-risk, mechanical extraction. No pushback needed on the overall direction; the only judgment calls are the shape of `AddAsync`'s transaction boundary and whether `RecurringJobSeeder` needs its own module-boundary test.

## Proposed Architecture

### Component Overview

```
Before:
Program.cs → SeedRecurringJobConfigurationsAsync() → IRecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync(jobs)
                                                        (Domain interface, EF-backed impl, startup-only method)

After:
Program.cs → SeedRecurringJobConfigurationsAsync() → IRecurringJobSeeder.SeedDefaultConfigurationsAsync(jobs)
                                                        │
                                                        ▼
                                              RecurringJobSeeder (Application/Features/BackgroundJobs/Services/)
                                                        │  builds RecurringJobConfiguration from IRecurringJob.Metadata
                                                        │  calls GetByJobNameAsync / AddAsync
                                                        ▼
                                              IRecurringJobConfigurationRepository (Domain — pure CRUD: Get/Add/Update)
                                                        │
                                                        ▼
                                              RecurringJobConfigurationRepository (Persistence, EF Core)
```

`RecurringJobDiscoveryService` (Hangfire hosted service) and the 4 MediatR handlers continue to depend only on `IRecurringJobConfigurationRepository` for CRUD — they are untouched by this refactor except for their stub updates.

### Key Design Decisions

#### Decision 1: Location and shape of `IRecurringJobSeeder`
**Options considered:**
- (a) Application/Features/BackgroundJobs/Services/ — matches spec and existing sibling services (`IHangfireJobEnqueuer`, `IHangfireRecurringJobScheduler`, `IRecurringJobStatusChecker`).
- (b) API-layer, since seeding is "startup infrastructure" — but this would force `RecurringJobSeeder`'s job-metadata-to-entity mapping logic (genuine domain/application concern) into the outer ring, alongside `ICurrentUserService`-style adapters that need `IHttpContextAccessor`. `RecurringJobSeeder` needs no web-only dependency, so there's no reason to push it there (unlike `CurrentUserService`, see ADR-005).

**Chosen approach:** (a), exactly as specified in FR-1.
**Rationale:** Consistent with `filesystem.md`'s "Features/{Feature}/Services/: Domain services and business logic" placement rule and the existing sibling services already in that folder. No new architectural category needed.

#### Decision 2: How `RecurringJobConfigurationRepository.AddAsync` commits
**Options considered:**
- (a) `AddAsync` calls `_context.RecurringJobConfigurations.AddAsync(...)` **and** `SaveChangesAsync()` itself (one commit per row), mirroring the existing `UpdateAsync` method, which does exactly this (add/update + `SaveChangesAsync` in the same call).
- (b) `AddAsync` only stages the entity (`_context.Add(...)`, no `SaveChangesAsync`), leaving `RecurringJobSeeder` to call `SaveChangesAsync` once after the loop — preserves the original single-batch-commit behavior exactly.

**Chosen approach:** (a) — commit inside `AddAsync`, matching `UpdateAsync`'s convention.
**Rationale:** `IRecurringJobConfigurationRepository`'s existing convention is "each public method is a complete unit of work" (`UpdateAsync` already commits itself; the repository doesn't expose `SaveChangesAsync` or `IUnitOfWork` to callers). Introducing a "stage now, someone else commits later" method would be the only member of this interface with that shape, adding a hidden ordering dependency between repository and caller. The spec explicitly permits either ("SaveChangesAsync is called once per added configuration or once per batch — either is acceptable"). Given the seed set is 9 jobs run once at startup, the per-row-commit cost (up to 9 round trips instead of 1) is immaterial and the interface stays uniform. If a future caller needs bulk-insert performance, that's a reason to add a distinct `AddRangeAsync`, not to change `AddAsync`'s contract.

#### Decision 3: Keep `SeedDefaultConfigurationsAsync` method name on the new interface
**Options considered:** Rename to `SeedAsync` (shorter, arguably more idiomatic for a single-purpose interface) vs. keep `SeedDefaultConfigurationsAsync`.
**Chosen approach:** Keep the existing name, per spec FR-1 and Out-of-Scope.
**Rationale:** This is a pure extraction PR; renaming multiplies the diff for no behavioral gain and makes the "moved, not rewritten" intent harder to verify in review. Agree with the spec's call.

## Implementation Guidance

### Directory / Module Structure

New file:
```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
```

Modified files:
```
backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs   (remove SeedDefaultConfigurationsAsync, add AddAsync)
backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs         (remove SeedDefaultConfigurationsAsync, add AddAsync)
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs               (register IRecurringJobSeeder)
backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs                              (resolve IRecurringJobSeeder instead of the repository, line ~458-463)
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs       (drop stub method)
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs        (drop stub method x2)
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs (remove the 2 seeding tests, add a minimal AddAsync test)
```

New test file:
```
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
```
This should follow the existing `RecurringJobConfigurationRepositoryTests` shape (in-memory `ApplicationDbContext`, real `RecurringJobConfigurationRepository`, real `RecurringJobSeeder` wrapping it) rather than mocking the repository — the two moved tests (`..._WhenEmpty_CreatesAllDefaultConfigurations`, `..._WhenConfigurationsExist_DoesNotDuplicate`) assert against `9` seeded configurations and no-duplicate semantics, which is most faithfully verified end-to-end through the real repository, exactly as it is today.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IRecurringJobSeeder
{
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

```csharp
// backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
}
```

`RecurringJobSeeder` depends only on `IRecurringJobConfigurationRepository` and `IRecurringJob` (both already Domain-layer types) — no EF Core, no `ApplicationDbContext`. This keeps the Application layer's dependency direction intact (Application → Domain, never → Persistence).

DI registration to add in `BackgroundJobsModule.cs`, immediately after the existing repository registration:
```csharp
services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();
```

### Data Flow

1. **Startup** (`Program.cs:169` → `SeedRecurringJobConfigurationsAsync`): creates a DI scope, resolves `IRecurringJobSeeder` (not the repository), resolves all `IRecurringJob` implementations, calls `seeder.SeedDefaultConfigurationsAsync(discoveredJobs)`. Logging/error/rethrow behavior at this call site is unchanged (FR-4).
2. **`RecurringJobSeeder.SeedDefaultConfigurationsAsync`**: for each `IRecurringJob`, builds a `RecurringJobConfiguration` from `job.Metadata` with `createdBy: "System"`; calls `_repository.GetByJobNameAsync(config.JobName)`; if `null`, calls `_repository.AddAsync(config)`. No change to which jobs get seeded or their default values.
3. **Runtime CRUD** (unchanged): `RecurringJobDiscoveryService`, `GetRecurringJobHandler`, `GetRecurringJobsListHandler`, `UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`, `RecurringJobStatusChecker` all continue to call `GetAllAsync` / `GetByJobNameAsync` / `UpdateAsync` directly on `IRecurringJobConfigurationRepository`, untouched by this refactor.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `AddAsync`'s per-call `SaveChangesAsync` changes seeding from 1 DB round trip to up to 9 | Low | Startup-only, 9 rows, one-time cost; acceptable per spec NFR-1. If job count grows materially, revisit with a batch `AddRangeAsync`. |
| Missed test double somewhere else in the repo still implementing the old interface member, causing a compile break not caught by a narrow grep | Low | `dotnet build` (already required by CLAUDE.md validation gate) will surface any interface-implementation mismatch as a compiler error — this is self-verifying, not just discoverable by grep. Confirmed via `grep -rl IRecurringJobConfigurationRepository backend/` that only the 3 known stubs + 5 Moq-based tests + production call sites exist. |
| `RecurringJobSeeder`'s new EF-free dependency boundary silently regresses (someone later adds an `ApplicationDbContext` reference to "simplify") | Low | Covered implicitly by the existing Clean Architecture project reference graph (Application does not reference Persistence/EF Core packages); no new enforcement needed for a single class, but flag in PR description that this class must stay EF-free. |
| Seeding tests moved to `RecurringJobSeederTests.cs` inadvertently drop coverage of `RecurringJobConfigurationRepository.AddAsync`'s own correctness (e.g. duplicate `JobName` handling at the repository level) | Low | FR-5/FR-2 already require a minimal direct `AddAsync` test in `RecurringJobConfigurationRepositoryTests.cs` — enforce this in review, don't let it get dropped as "redundant" with the seeder test. |

## Specification Amendments

None required — the spec is accurate and fully grounded against the current code (verified independently: interface members, implementation body, call site line numbers, and all three affected stubs all match what's described). Two clarifications worth carrying into the implementation PR description (not spec changes, since the spec already permits either choice):

1. **`AddAsync` commits per call** (Decision 2 above) — make this explicit in the PR so a reviewer doesn't mistake it for an accidental N+1-write regression.
2. **`RecurringJobSeederTests.cs` should use the real repository + in-memory DB**, not a mock, to preserve the existing tests' end-to-end assertion style (asserting final DB state via `GetAllAsync`, not verifying mock call counts).

## Prerequisites

None. No new configuration, no migration (schema unchanged — confirmed no changes needed to `RecurringJobConfigurationConfiguration.cs` or `Migrations/`), no new package dependency. This can be implemented directly against the current `main`/feature branch state.
