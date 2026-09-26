# Batch-load existing configurations in RecurringJobSeeder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the N+1 query pattern in `RecurringJobSeeder.SeedDefaultConfigurationsAsync` by loading all existing `RecurringJobConfiguration` rows once via `GetAllAsync` and looking them up from an in-memory dictionary instead of calling `GetByJobNameAsync` once per job.

**Architecture:** No new components. `RecurringJobSeeder` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`) is edited in place to call `_repository.GetAllAsync(cancellationToken)` once before its `foreach` loop and build a `Dictionary<string, RecurringJobConfiguration>` keyed by `JobName` (`StringComparer.Ordinal`) for `TryGetValue` lookups inside the loop, mirroring the existing pattern in `RecurringJobDiscoveryService.StartAsync`. Create/update semantics, the public method signature, and all collaborating interfaces are unchanged.

**Tech Stack:** .NET 8, xUnit, Entity Framework Core InMemory provider, `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

---

### task: add-query-count-regression-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

This task adds a failing regression test that proves the N+1 pattern exists today, using the same "counting repository wrapper" pattern already established in this codebase for query-count assertions (see `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`). It intentionally runs and fails against the current implementation before Task 2 fixes it.

- [ ] **Step 1: Add a `CountingRepositoryWrapper` private nested class to `RecurringJobSeederTests`**

Add this class inside `RecurringJobSeederTests`, alongside the existing `MockRecurringJob` private class (after it, before the closing brace of the outer class):

```csharp
    /// <summary>
    /// Wrapper around IRecurringJobConfigurationRepository that counts calls to
    /// GetAllAsync and GetByJobNameAsync, to verify the seeder issues a single
    /// batch read instead of one read per job.
    /// </summary>
    private sealed class CountingRepositoryWrapper : IRecurringJobConfigurationRepository
    {
        private readonly IRecurringJobConfigurationRepository _inner;

        public int GetAllAsyncCallCount { get; private set; }
        public int GetByJobNameAsyncCallCount { get; private set; }

        public CountingRepositoryWrapper(IRecurringJobConfigurationRepository inner)
        {
            _inner = inner;
        }

        public async Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllAsyncCallCount++;
            return await _inner.GetAllAsync(cancellationToken);
        }

        public async Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
        {
            GetByJobNameAsyncCallCount++;
            return await _inner.GetByJobNameAsync(jobName, cancellationToken);
        }

        public Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => _inner.AddAsync(configuration, cancellationToken);

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(configuration, cancellationToken);
    }
```

- [ ] **Step 2: Add the failing test**

Add this test method inside `RecurringJobSeederTests`, after `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`:

```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups()
    {
        // Arrange - one pre-existing configuration, plus several jobs with no existing row,
        // so both the "found" and "not found" branches execute during the same run.
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            "Europe/Prague",
            true,
            "System",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var countingRepository = new CountingRepositoryWrapper(_repository);
        var seeder = new RecurringJobSeeder(countingRepository, _timeProvider);
        var mockJobs = CreateMockJobs();

        // Act
        await seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert - regardless of how many jobs are seeded, exactly one batch read must occur
        // and no per-job lookup may occur.
        Assert.Equal(1, countingRepository.GetAllAsyncCallCount);
        Assert.Equal(0, countingRepository.GetByJobNameAsyncCallCount);
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups"`

Expected: FAIL. `GetAllAsyncCallCount` is `0` (current implementation never calls `GetAllAsync`) and/or `GetByJobNameAsyncCallCount` is `9` (one call per mock job), not `0`.

- [ ] **Step 4: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "test(background-jobs): add failing query-count regression test for RecurringJobSeeder N+1"
```

---

### task: batch-load-existing-configurations

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:41-59`

This task implements the fix: replace the per-job `GetByJobNameAsync` call inside the loop with a single `GetAllAsync` call and dictionary lookup, making the Task 1 test (and all five pre-existing tests) pass.

- [ ] **Step 1: Replace the loop body in `SeedDefaultConfigurationsAsync`**

Replace lines 41-59 of `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` (the `foreach (var config in defaultConfigurations)` block) with:

```csharp
        var existing = await _repository.GetAllAsync(cancellationToken);
        var existingByName = existing.ToDictionary(c => c.JobName, StringComparer.Ordinal);

        foreach (var config in defaultConfigurations)
        {
            if (!existingByName.TryGetValue(config.JobName, out var existingConfig))
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else
            {
                existingConfig.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existingConfig.CronExpression,   // preserve admin override
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existingConfig, cancellationToken);
            }
        }
```

The full method (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`, lines 25-60) must read exactly as follows after this change:

```csharp
    public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.TimeZoneId,
            job.Metadata.DefaultIsEnabled,
            "System",
            now
        )).ToArray();

        // Load all existing configs in one query
        var existing = await _repository.GetAllAsync(cancellationToken);
        var existingByName = existing.ToDictionary(c => c.JobName, StringComparer.Ordinal);

        foreach (var config in defaultConfigurations)
        {
            if (!existingByName.TryGetValue(config.JobName, out var existingConfig))
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else
            {
                existingConfig.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existingConfig.CronExpression,   // preserve admin override
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existingConfig, cancellationToken);
            }
        }
    }
```

Note the one required behavioral-equivalence fix from the raw literal: since `existing` is no longer fetched fresh per job, the pre-existing entity variable (`existingConfig`, matched from the dictionary) is the one both read from and written back to — `existingConfig.CronExpression` (not a separate freshly-fetched entity's `CronExpression`) is passed to `UpdateConfiguration` to preserve the admin override, exactly matching the original method's `existing.CronExpression` usage.

- [ ] **Step 2: Run the previously-failing test to verify it now passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups"`

Expected: PASS.

- [ ] **Step 3: Run the full `RecurringJobSeederTests` suite to verify no regressions**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"`

Expected: PASS, all 6 tests (the 5 pre-existing tests plus the new one from Task 1).

- [ ] **Step 4: Run `dotnet build` and `dotnet format` across the backend**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings or errors.

Run: `cd backend && dotnet format`
Expected: Exits cleanly; if it reformats `RecurringJobSeeder.cs` or the test file, review the diff to confirm only whitespace/style changed, then re-run Step 3 to confirm tests still pass.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: PASS — no test outside `RecurringJobSeederTests` should be affected by this change, since no interface, DI registration, or call site outside `RecurringJobSeeder.cs` was touched.

- [ ] **Step 6: Commit the fix**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
git commit -m "fix(background-jobs): batch-load existing configs in RecurringJobSeeder to eliminate N+1 queries

Replaces the per-job GetByJobNameAsync call inside the seeding loop with a
single GetAllAsync call plus an in-memory dictionary lookup, mirroring the
existing pattern in RecurringJobDiscoveryService.StartAsync. Reduces startup
seeding from N+1 sequential DB reads to 1.

Closes #4317"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (single `GetAllAsync` call, no per-job `GetByJobNameAsync`) → `batch-load-existing-configurations` Step 1, verified by `add-query-count-regression-test`.
- FR-2 (create/update semantics preserved exactly) → `batch-load-existing-configurations` Step 3 (full existing test suite passes unmodified) plus the admin-override preservation note in Step 1.
- FR-3 (no signature/call-site changes) → `batch-load-existing-configurations` Step 1 only touches the method body; Step 5 (full backend suite) confirms no other file needed a change.
- NFR-1 (performance: 1 read instead of N) → directly asserted by the `add-query-count-regression-test` task.
- NFR-2 (security: no change) → no new inputs introduced anywhere in the plan.

**Placeholder scan:** No TBD/TODO markers; every step shows complete, exact code and exact commands with expected output.

**Type consistency:** `CountingRepositoryWrapper` implements `IRecurringJobConfigurationRepository`'s four methods (`GetAllAsync`, `GetByJobNameAsync`, `AddAsync`, `UpdateAsync`) exactly as declared in `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`; `RecurringJobSeeder`'s constructor signature (`IRecurringJobConfigurationRepository repository, TimeProvider timeProvider`) is unchanged and is what both the existing tests and the new test construct it with.
