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

