### task: add-timezone-forwarding-regression-tests


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`

- [ ] **Step 1: Add the three new tests**

Insert all three tests into `HangfireRecurringJobSchedulerTests` immediately **after** the closing brace of `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` (currently line 120) and **before** `public void Dispose()` (currently line 122).

The discovery-registration arrange block is repeated verbatim from the two existing tests in this file — that is deliberate, it matches the file's existing style; do not refactor the existing tests into a shared helper as part of this change.

All required `using` directives are already at the top of the file (`Hangfire`, `Hangfire.Storage`, `Microsoft.AspNetCore.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`, `Anela.Heblo.Xcc`, `Xunit`). Add none.

```csharp
    [Fact]
    public async Task UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone()
    {
        // Arrange — register via discovery (startup path), which uses the metadata
        // time zone "Europe/Prague" for parity-test-job
        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var discovery = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions);
        await discovery.StartAsync(CancellationToken.None);

        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act — pass a time zone that deliberately differs from the job's metadata value.
        // "UTC" resolves on Linux containers and on Windows without ICU.
        scheduler.UpdateCronSchedule("parity-test-job", "0 5 * * *", "UTC");

        // Assert — the argument, not IRecurringJob.Metadata, drove the registration
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");
        Assert.Equal("0 5 * * *", job.Cron);
        Assert.Equal("UTC", job.TimeZoneId);
        Assert.NotEqual("Europe/Prague", job.TimeZoneId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException(string? timeZoneId)
    {
        // Arrange
        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act + Assert — ThrowsAny, not Throws: ArgumentException.ThrowIfNullOrWhiteSpace(null)
        // throws ArgumentNullException, and xUnit's Assert.Throws<T> matches the exact type.
        Assert.ThrowsAny<ArgumentException>(() =>
            scheduler.UpdateCronSchedule("parity-test-job", "0 6 * * *", timeZoneId!));

        // Assert — the guard runs before any side effect, so nothing was written to storage
        using var connection = JobStorage.Current.GetConnection();
        Assert.DoesNotContain(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");
    }

    [Fact]
    public async Task UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged()
    {
        // Arrange — register via discovery: cron "0 0 * * *" (metadata default,
        // the stub repository returns no DB rows), time zone "Europe/Prague"
        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var discovery = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions);
        await discovery.StartAsync(CancellationToken.None);

        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act — a bogus zone id makes TimeZoneInfo.FindSystemTimeZoneById throw
        // TimeZoneNotFoundException inside HangfireJobRegistrationHelper
        var exception = Record.Exception(() =>
            scheduler.UpdateCronSchedule("parity-test-job", "0 9 * * *", "Not/AZone"));

        // Assert — fire-and-forget: the failure is logged, never rethrown
        Assert.Null(exception);

        // Assert — the stored record is untouched: previous cron and previous time zone
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");
        Assert.Equal("0 0 * * *", job.Cron);
        Assert.Equal("Europe/Prague", job.TimeZoneId);
    }
```

Do not add any `GlobalConfiguration`/`JobStorage` setup inside these tests — the shared `HangfireTestFixture` (via `[Collection("Hangfire")]`, which also sets `DisableParallelization = true`) configures `UseMemoryStorage()` once for the whole run, and the existing `Dispose()` on this class removes every recurring job between tests.

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors and no new warnings. In particular there must be **no** `CS8625` (null literal to non-nullable) — the theory parameter is declared `string?` and the call site uses the `timeZoneId!` null-forgiving operator, which is required because both `Anela.Heblo.Tests` and `Anela.Heblo.API` have `<Nullable>enable</Nullable>`.

- [ ] **Step 3: Run the three new tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~HangfireRecurringJobSchedulerTests"`

Expected: `Passed!` with 0 failed and 8 tests run (3 pre-existing facts + 1 new fact `UsesPassedTimeZone` + 3 theory rows for `WithMissingTimeZoneId` + 1 new fact `WithUnresolvableTimeZone`).

- [ ] **Step 4: Prove `UsesPassedTimeZone_NotJobMetadataTimeZone` is load-bearing**

This regression test must fail against the pre-change adapter, otherwise it guards nothing. Verify it once, then restore:

1. In `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`, temporarily change the `RegisterOrUpdate` call from:

```csharp
            HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, timeZoneId);
```

to (simulating the old metadata read):

```csharp
            HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, "Europe/Prague");
```

2. Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone"`

Expected: **1 failed**, with `Assert.Equal() Failure: Expected: UTC, Actual: Europe/Prague`.

3. Revert the temporary edit with: `git checkout -- backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

4. Re-run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone"`

Expected: `Passed!` with 1 passed.

5. Run `git status` and confirm `HangfireRecurringJobScheduler.cs` is **not** listed as modified before committing.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
git commit -m "test(background-jobs): add timeZoneId forwarding, guard and unresolvable-zone regression tests"
```
