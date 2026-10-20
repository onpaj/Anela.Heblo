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
