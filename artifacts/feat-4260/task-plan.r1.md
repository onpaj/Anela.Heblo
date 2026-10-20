# Complete the `ICronScheduler.UpdateCronSchedule` Contract with `timeZoneId` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a third `string timeZoneId` parameter to `ICronScheduler.UpdateCronSchedule`, pass the value the caller already holds (`RecurringJobConfiguration.TimeZoneId`), and stop `HangfireRecurringJobScheduler` from service-locating `IRecurringJob.Metadata.TimeZoneId` out of the DI container.

**Architecture:** Consumer-owned port (`Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`) implemented by an adapter (`Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`) and bound with `AddSingleton` in `ServiceCollectionExtensions.cs:376` (unchanged). The port is widened by one positional parameter; the single call site (`UpdateRecurringJobCronHandler.cs:74`) forwards the entity's `TimeZoneId`; the adapter keeps its `IServiceProvider` **only** for `jobName → Type` resolution (required by `HangfireJobRegistrationHelper.RegisterOrUpdate`, which closes a generic `RecurringJob.AddOrUpdate<TJob>` by reflection) and stops reading job metadata. No DB schema change, no HTTP/OpenAPI change, no frontend change, no new files.

**Tech Stack:** .NET 8, C# (nullable enabled in both `Anela.Heblo.API` and `Anela.Heblo.Tests`), Hangfire 1.8 (`RecurringJob.AddOrUpdate`, `RecurringJobOptions`, `JobStorage`), `Microsoft.Extensions.DependencyInjection`, xUnit + Moq + FluentAssertions, `Hangfire.MemoryStorage` via the shared `HangfireTestFixture` / `[Collection("Hangfire")]`.

---

### task: widen-icronscheduler-port-and-call-site

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs:1-11`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs:74`

- [ ] **Step 1: Replace the whole contents of `ICronScheduler.cs` with the widened, documented port**

The file is currently exactly this (11 lines):

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running Hangfire job schedule immediately,
/// without requiring a restart.
/// </summary>
public interface ICronScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression);
}
```

Replace it with exactly this:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running job schedule immediately,
/// without requiring a restart.
/// Fire-and-forget: implementations log and swallow scheduling failures (unknown job,
/// unresolvable time zone, storage error) rather than throwing. The one exception is
/// invalid arguments, which throw before any side effect occurs.
/// The caller owns the time zone: implementations must not re-derive it from any other
/// source, such as job metadata.
/// </summary>
public interface ICronScheduler
{
    /// <param name="jobName">Registered job name; also the Hangfire recurring-job id.</param>
    /// <param name="cronExpression">Validated 5- or 6-field CRON expression.</param>
    /// <param name="timeZoneId">System/IANA time-zone id the schedule is evaluated in; must be resolvable on the host via TimeZoneInfo.FindSystemTimeZoneById.</param>
    /// <exception cref="ArgumentException">Any argument is null, empty or whitespace.</exception>
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
```

Do **not** add any `using` directive to this file — it has none today and must still have none. No Hangfire, ASP.NET Core or `IServiceProvider` type may appear in it.

- [ ] **Step 2: Confirm the file still has zero `using` directives**

Run: `grep -c "^using" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

Expected output: `0`

- [ ] **Step 3: Pass the entity's `TimeZoneId` at the single call site**

In `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`, the current lines 71–74 are:

```csharp
            job.UpdateCronExpression(request.CronExpression, modifiedBy, now);
            await _repository.UpdateAsync(job, cancellationToken);

            _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression);
```

Change to:

```csharp
            job.UpdateCronExpression(request.CronExpression, modifiedBy, now);
            await _repository.UpdateAsync(job, cancellationToken);

            _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId);
```

Change **nothing else** in this file: the CRON validation, the not-found branch, the `try`/`catch`, the response construction, the logging statements and the ordering (scheduler call strictly after `_repository.UpdateAsync`) all stay exactly as they are. Do not add a repository call, a DI resolution or a configuration lookup to obtain the time zone — `job` is the already-loaded `RecurringJobConfiguration` and carries `TimeZoneId`.

- [ ] **Step 4: Build the Application project**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: `Build succeeded.` with 0 errors and no new warnings.

Note: `cd backend && dotnet build` (the whole solution) is **expected to fail** at this point with `CS0535: 'HangfireRecurringJobScheduler' does not implement interface member 'ICronScheduler.UpdateCronSchedule(string, string, string)'` plus `CS1501` errors in the two test files. That is intentional — the next two tasks fix the adapter and the tests. Build only the Application project in this task.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs
git commit -m "refactor(background-jobs): add timeZoneId to ICronScheduler.UpdateCronSchedule and forward it from the handler"
```

---

### task: update-hangfire-adapter-to-use-passed-timezone

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs:8-61`

- [ ] **Step 1: Extend the class-level XML doc to record who owns the time zone**

The current class doc (lines 8–12) is:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// </summary>
```

Change it to:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// The caller owns the time zone: it comes from the <c>RecurringJobConfiguration</c> DB row
/// and is never re-derived from <c>IRecurringJob.Metadata</c>. That is equivalent today because
/// <c>RecurringJobSeeder</c> re-syncs each row's <c>TimeZoneId</c> from metadata on every startup,
/// before <see cref="RecurringJobDiscoveryService"/> registers anything. If <c>TimeZoneId</c> ever
/// becomes admin-editable, the startup discovery path must switch to the DB value as well and the
/// seeder must stop re-syncing that field.
/// </summary>
```

- [ ] **Step 2: Replace the `UpdateCronSchedule` method body**

The current method (lines 26–61) is:

```csharp
    public void UpdateCronSchedule(string jobName, string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in DI — Hangfire schedule not updated live", jobName);
            return;
        }

        try
        {
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                job.GetType(),
                jobName,
                cronExpression,
                job.Metadata.TimeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update live Hangfire schedule for {JobName}. " +
                "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
                jobName, job.Metadata.TimeZoneId);
            return;
        }

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression}",
            jobName, cronExpression);
    }
```

Replace it with exactly this:

```csharp
    public void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        // Resolves the runtime Type for jobName, which HangfireJobRegistrationHelper.RegisterOrUpdate
        // requires to close its generic RecurringJob.AddOrUpdate<TJob> overload. A scope is used (not a
        // direct IEnumerable<IRecurringJob> injection) because this adapter is a singleton and IRecurringJob
        // implementations are scoped. Job metadata is deliberately not read here — the schedule's time zone
        // comes from the caller.
        using var scope = _serviceProvider.CreateScope();
        var jobType = scope.ServiceProvider.GetServices<IRecurringJob>()
            .FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();

        if (jobType == null)
        {
            _logger.LogWarning(
                "Job {JobName} has no registered IRecurringJob implementation in DI — " +
                "its Hangfire schedule was not updated live. The DB configuration row was still saved; " +
                "the new schedule will apply on the next application start if the job type is restored.",
                jobName);
            return;
        }

        try
        {
            HangfireJobRegistrationHelper.RegisterOrUpdate(jobType, jobName, cronExpression, timeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update live Hangfire schedule for {JobName}. " +
                "TimeZone '{TimeZoneId}' may be invalid or unsupported on this host.",
                jobName, timeZoneId);
            return;
        }

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression} ({TimeZoneId})",
            jobName, cronExpression, timeZoneId);
    }
```

Points that are load-bearing and must not be "tidied":
- Guard order follows parameter order (`jobName`, `cronExpression`, `timeZoneId`), so a caller's wrong argument produces a matching failure.
- All three guards run **before** `_serviceProvider.CreateScope()`, so an invalid argument causes no side effect and no scope creation.
- `_serviceProvider` and its `ArgumentNullException` guard in the constructor (lines 18–24) stay untouched.
- Exactly one `CreateScope()`, one `GetServices<IRecurringJob>()` enumeration and one `RegisterOrUpdate` call per invocation (NFR-1).
- Do not add `throw` to the `catch` — the method stays fire-and-forget (`void`), because the handler calls it after the DB commit point.

- [ ] **Step 3: Confirm no metadata time-zone read remains**

Run: `grep -n "Metadata.TimeZoneId" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Expected output: no matches (exit code 1). The only remaining `Metadata` reference in this file must be `j.Metadata.JobName` inside the `FirstOrDefault` predicate — verify with:

Run: `grep -n "Metadata" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs`

Expected output: exactly two lines — the `/// ... never re-derived from <c>IRecurringJob.Metadata</c> ...` doc line and the `.FirstOrDefault(j => j.Metadata.JobName == jobName)?.GetType();` line, plus the `// ... Job metadata is deliberately not read here ...` comment line (three lines total, one of them the lowercase-`metadata` comment).

- [ ] **Step 4: Build the API project**

Run: `cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj`

Expected: `Build succeeded.` with 0 errors and no new warnings.

Note: `cd backend && dotnet build` on the whole solution still fails at this point, because the two test files still call the two-argument overload. The next task fixes that.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
git commit -m "refactor(background-jobs): use the caller-supplied timeZoneId in HangfireRecurringJobScheduler"
```

---

### task: update-existing-tests-to-three-argument-signature

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs:61,78,125`
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs:42,67,107`

- [ ] **Step 1: Update the two "never called" verifications in the handler tests**

In `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs`, line 61 (inside `Handle_WhenJobNotFound_ReturnsNotFoundError`) and line 78 (inside `Handle_WhenCronExpressionInvalid_ReturnsBadRequest`) are both currently:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
```

Change **both** lines to:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
```

`It.IsAny<string>()` is correct here — these assert the scheduler was **never** called, so there is no concrete value to pin.

- [ ] **Step 2: Strengthen the happy-path verification to assert the concrete forwarded time zone**

Still in `UpdateRecurringJobCronHandlerTests.cs`, line 125 (inside `Handle_WhenValidCron_UpdatesDbAndHangfire`) is currently:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron), Times.Once);
```

Change to:

```csharp
        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron, "Europe/Prague"), Times.Once);
```

`"Europe/Prague"` is the exact literal the `CreateTestJob` helper at the bottom of this same file builds the entity with (`timeZoneId: "Europe/Prague"`, line 154). Do **not** use `It.IsAny<string>()` for the third argument here — asserting the concrete value is what proves the handler forwards the entity's `TimeZoneId` rather than some other source, and it is the mitigation for a future `cron`/`timeZoneId` transposition.

- [ ] **Step 3: Pass the metadata time zone at the three existing scheduler-test call sites**

In `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`, make these three edits. `"Europe/Prague"` is `ParityTestRecurringJob.Metadata.TimeZoneId` (line 150 of the same file), so these three tests keep asserting exactly what they asserted before.

Line 42, currently:

```csharp
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *");
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *", "Europe/Prague");
```

Line 67, currently:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", newCron);
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", newCron, "Europe/Prague");
```

Line 107, currently:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *");
```

becomes:

```csharp
        scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *", "Europe/Prague");
```

Do not delete, rename or weaken any assertion in these three tests. In particular `Assert.Equal("Europe/Prague", job.TimeZoneId)` on line 73 and `Assert.Equal(discoveredTimeZoneId, afterUpdate.TimeZoneId)` on line 116 stay exactly as they are.

- [ ] **Step 4: Build the whole solution — it must now succeed**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors and no new warnings. This is the first point in the plan where the full solution compiles again.

- [ ] **Step 5: Run the BackgroundJobs tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs"`

Expected: `Passed!` with 0 failed. In particular `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage`, `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration`, `UpdateCronSchedule_WithUnknownJobName_LogsWarningAndReturns` and all five `UpdateRecurringJobCronHandlerTests` must pass.

Be aware (this is expected and not a defect): after this change, `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage` and `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` pass only because the tests now hand `"Europe/Prague"` in themselves. They no longer prove that the runtime path derives the same time zone as the startup path — they prove the adapter forwards what it was given. The next two tasks restore that parity guarantee with real assertions.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
git commit -m "test(background-jobs): update ICronScheduler call sites to the three-argument signature"
```

---

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

---

### task: add-seeder-timezone-resync-assertion

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

Rationale (arch-review amendment A-3): with the adapter now forwarding whatever it is given, the guarantee that the runtime CRON-edit path applies the *same* time zone as the startup discovery path rests entirely on `RecurringJobSeeder` re-syncing each existing row's `TimeZoneId` from `IRecurringJob.Metadata.TimeZoneId` on every boot. `RecurringJobSeederTests` currently asserts that `TimeZoneId` is seeded correctly for **new** rows (lines 55–60) but never asserts the re-sync for an **existing** row. Nothing else in the suite guards that link.

- [ ] **Step 1: Add the re-sync test**

Insert this test into `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` immediately after `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem` (currently ends at line 183) and before `public void Dispose()` (currently line 185).

The `CreateMockJobs()` helper at the bottom of the file already builds `invoice-classification` with `timeZoneId: "America/New_York"` (line 200), while every other mock job uses `RecurringJobMetadata.DefaultTimeZoneId` (`"Europe/Prague"`). That makes `invoice-classification` the right job for this test: the arranged row starts on `"Europe/Prague"` and must end on `"America/New_York"`.

No new `using` directive is needed — `Anela.Heblo.Domain.Features.BackgroundJobs` and `Xunit` are already imported at the top of the file.

```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata()
    {
        // Arrange - existing row carries a stale TimeZoneId that no longer matches the code metadata
        // ("invoice-classification" metadata says "America/New_York")
        var existingConfig = new RecurringJobConfiguration(
            "invoice-classification",
            "Invoice Classification",
            "Classifies and categorizes incoming invoices",
            "0 * * * *",
            "Europe/Prague", // stale - developer-owned field, must be overwritten from metadata
            true,
            "System",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert - TimeZoneId is developer-owned and re-synced from metadata on every seed run.
        // This invariant is what makes the runtime CRON-update path (which reads TimeZoneId from
        // the DB row) equivalent to the startup discovery path (which reads it from metadata).
        var updated = await _repository.GetByJobNameAsync("invoice-classification");
        Assert.NotNull(updated);
        Assert.Equal("America/New_York", updated!.TimeZoneId);
    }
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors and no new warnings.

- [ ] **Step 3: Run the seeder tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJobSeederTests"`

Expected: `Passed!` with 0 failed and 6 tests run (5 pre-existing + the new one).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "test(background-jobs): assert RecurringJobSeeder re-syncs an existing row's TimeZoneId from metadata"
```

---

### task: verify-build-format-and-acceptance-criteria

**Files:**
- None modified — this task runs verification commands against the five files changed by the four prior tasks, and only edits anything if `dotnet format` reports a violation.

- [ ] **Step 1: Confirm no two-argument call site survives anywhere in `backend/`**

Run: `grep -rn "UpdateCronSchedule(" backend/src backend/test`

Expected: every match is either a test **method name** (`UpdateCronSchedule_...`) or a declaration/call with **three** arguments. Specifically the declarations must read `UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)` (in `ICronScheduler.cs` and `HangfireRecurringJobScheduler.cs`), and every call must have three comma-separated arguments. There must be no match of the form `UpdateCronSchedule(x, y)`.

- [ ] **Step 2: Confirm the Application layer gained no Hangfire coupling (NFR-3, as amended by A-1)**

Run: `grep -rn "^using Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/`

Expected output: no matches (exit code 1). (Note: `Anela.Heblo.Application.csproj:12` already carries a `Hangfire.Core` package reference for other features — that is pre-existing and out of scope. The checkable criterion is that no file under `Features/BackgroundJobs/` in the Application project gains a Hangfire `using`.)

Run: `grep -c "^using" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`

Expected output: `0`

- [ ] **Step 3: Confirm the DI registration and lifetime are unchanged**

Run: `grep -n "ICronScheduler" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

Expected output: exactly one line — `services.AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>();` (around line 376), unmodified.

- [ ] **Step 4: Confirm the handler's blast radius is one line (FR-2)**

Run: `git diff HEAD~5 -- backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`

Expected: exactly one removed line and one added line, both the `_scheduler.UpdateCronSchedule(...)` statement. No change to the validation, the not-found branch, the `try`/`catch`, the response construction, the logging, or the ordering relative to `await _repository.UpdateAsync(job, cancellationToken);`.

- [ ] **Step 5: Confirm the change touched only the expected files (NFR-2, NFR-4, FR-6)**

Run: `git diff --name-only HEAD~5`

Expected output: exactly these six paths and nothing else —

```
backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs
```

In particular there must be **no** controller file, no `*Request.cs`/`*Response.cs` DTO, no EF migration under `backend/src/Anela.Heblo.Persistence/Migrations/`, no file under `frontend/`, and no file under `docs/`. `docs/superpowers/plans/2026-03-29-db-driven-cron-config.md` and `docs/superpowers/plans/2026-05-27-consolidate-hangfire-recurringjob-registration.md` show the old two-parameter signature and must be left alone — they are point-in-time records of completed work, not living specs.

- [ ] **Step 6: Full solution build**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors. The warning count must not increase versus the pre-change baseline.

- [ ] **Step 7: Format check**

Run: `cd backend && dotnet format --verify-no-changes`

Expected: exits 0 with no output. If it reports violations in any of the touched files, run `cd backend && dotnet format` (without `--verify-no-changes`), then re-stage the affected file and `git commit --amend` onto whichever of the prior tasks' commits introduced it — do not add a separate "fix formatting" commit for a change this small.

- [ ] **Step 8: Run the full BackgroundJobs + Hangfire test surface**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs|FullyQualifiedName~Hangfire"`

Expected: `Passed!` with 0 failed. This must include, all green:
- `UpdateRecurringJobCronHandlerTests` — all 5 tests, with the happy path asserting `UpdateCronSchedule("my-job", newCron, "Europe/Prague")` exactly once.
- `HangfireRecurringJobSchedulerTests` — the 3 pre-existing tests plus `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone`, the 3 rows of `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException`, and `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged`.
- `RecurringJobSeederTests` — the 5 pre-existing tests plus `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`.
- `RecurringJobDiscoveryServiceTests`, `HangfireJobEnqueuerTests`, `HangfireFailedJobCounterTests` — untouched, must still pass.

- [ ] **Step 9: Run the full backend test suite**

Run: `cd backend && dotnet test`

Expected: all tests pass, 0 failed. No frontend build and no E2E run are required for this change — there is no HTTP contract change, so the generated TypeScript client produces an empty diff.

- [ ] **Step 10: Final gate — working tree clean**

Run: `git status`

Expected: clean working tree (nothing to commit), unless Step 7 required a formatting fix that was amended into a prior commit — in which case re-run `git status` after the amend and confirm it is clean then.

---

## Self-Review

**Spec coverage (FR-1 … FR-6, NFR-1 … NFR-4):**

- **FR-1** (interface gains `string timeZoneId`, last, non-nullable; XML docs state IANA/host-resolvable, fire-and-forget, failures logged not surfaced; same file/namespace; no new `using`; build clean) → `widen-icronscheduler-port-and-call-site` Steps 1, 2, 4; re-verified in `verify-build-format-and-acceptance-criteria` Steps 2 and 6.
- **FR-2** (call site passes `job.TimeZoneId`; no other handler line changes; no extra lookup; scheduler call stays after `UpdateAsync`; response shape unchanged) → `widen-icronscheduler-port-and-call-site` Step 3; verified byte-for-byte in `verify-build-format-and-acceptance-criteria` Step 4, and behaviourally by `Handle_WhenValidCron_UpdatesDbAndHangfire` (`update-existing-tests-to-three-argument-signature` Step 2).
- **FR-3** (new signature; third guard before any DI scope; metadata time zone no longer read; error log uses passed `timeZoneId`; `IServiceProvider` + its null guard + `AddSingleton` registration retained; improved warning text; still delegates to `HangfireJobRegistrationHelper`; `dotnet format` clean) → `update-hangfire-adapter-to-use-passed-timezone` Steps 1–4; the "no `Metadata.TimeZoneId`" criterion is Step 3 there; the registration criterion is `verify-…` Step 3; the format criterion is `verify-…` Step 7. The guard-before-side-effect criterion is asserted by `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException` (`add-timezone-forwarding-regression-tests` Step 1). The unregistered-job criterion is asserted by the pre-existing `UpdateCronSchedule_WithUnknownJobName_LogsWarningAndReturns`. The caught-and-not-rethrown criterion is asserted by `UpdateCronSchedule_WithUnresolvableTimeZone_LogsErrorAndLeavesScheduleUnchanged`. The record-structure-parity criterion is the pre-existing `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration`.
- **A-4** (arch-review addition: success log must include `{TimeZoneId}`) → `update-hangfire-adapter-to-use-passed-timezone` Step 2, the `LogInformation` template now reads `"Live Hangfire schedule updated for {JobName} → {CronExpression} ({TimeZoneId})"`.
- **FR-4** (behaviour parity; the two named existing tests still pass) → `update-existing-tests-to-three-argument-signature` Steps 3 and 5. Per amendment **A-3**, the note in Step 5 states explicitly that those two assertions become tautological, and the parity guarantee is re-established by `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` (adapter forwards the argument), the strengthened handler verification (handler forwards the entity's value), and the new `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata` (seeder keeps entity and metadata equal) in `add-seeder-timezone-resync-assertion`.
- **FR-5** (lines 61/78/125 and 42/67/107 updated, no test deleted or weakened; three new tests; targeted test run green; no `It.IsAny<string>()` for `timeZoneId` on the happy path; no two-argument call site left) → `update-existing-tests-to-three-argument-signature` Steps 1–3 and 5, `add-timezone-forwarding-regression-tests` Steps 1 and 3, `verify-…` Steps 1 and 8. Amendment **A-2** is honoured: the theory parameter is `string?`, `[InlineData(null)]` is passed with `timeZoneId!`, and the assertion is `Assert.ThrowsAny<ArgumentException>` (not `Assert.Throws`), because `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException`. Amendment **Q4** is honoured: `"UTC"` is the contrasting zone.
- **FR-6** (no doc changes; plan documents left alone) → `verify-…` Step 5 asserts `git diff --name-only` contains no `docs/` path and names the two plan files that must stay untouched.
- **NFR-1** (exactly one scope, one enumeration, one `AddOrUpdate` per call) → enforced by the exact method body given in `update-hangfire-adapter-to-use-passed-timezone` Step 2 and its "must not be tidied" list.
- **NFR-2** (no controller/policy/feature-flag change; logged values limited to job name, cron, time zone) → `verify-…` Step 5 (no controller file in the diff) plus the three log templates written out verbatim in `update-hangfire-adapter-to-use-passed-timezone` Step 2, which carry only `{JobName}`, `{CronExpression}`, `{TimeZoneId}`.
- **NFR-3** as amended by **A-1** (checkable form: `ICronScheduler.cs` gains no `using` and no Hangfire/ASP.NET/`IServiceProvider` type in its signature; no file under Application `Features/BackgroundJobs/` gains a Hangfire `using`; the retained `IServiceProvider` carries a comment naming what it is for) → `verify-…` Step 2 for the greps, `update-hangfire-adapter-to-use-passed-timezone` Steps 1–2 for both the class XML doc (Decision 3 provenance) and the inline `CreateScope()` justification comment (Decision 2).
- **NFR-4** (no OpenAPI change, no TS client diff, no EF migration) → `verify-…` Step 5 (diff contains no DTO, controller, migration or `frontend/` path) and Step 9's note.
- **Q1/Q2/Q3** — all three resolved as "keep current shape" by the arch review; the plan contains no task that removes `IServiceProvider`, changes the `void` return, or touches `RecurringJobDiscoveryService`. The follow-up issues (a `jobName → Type` registry; a `202`-style "saved, applies after restart" response) are deliberately not in this plan.

**Placeholder scan:** No "TBD", "TODO", "implement later", "add appropriate error handling", "handle edge cases" or "similar to Task N" appears. Every code step shows the complete before/after source, every verification step gives an exact command with its expected output, and the discovery-registration arrange block is repeated in full in each new test rather than cross-referenced.

**Type consistency:** `UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId)` is used identically in the interface (task 1), the adapter (task 2), the six updated call sites (task 3) and the four new call sites (task 4). `HangfireJobRegistrationHelper.RegisterOrUpdate(Type jobType, string jobName, string cronExpression, string timeZoneId)` is quoted exactly as it exists at `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs:22-26`. `RecurringJobConfiguration`'s constructor argument order used in the new seeder test (`jobName, displayName, description, cronExpression, timeZoneId, isEnabled, lastModifiedBy, lastModifiedAt`) matches both the entity and the five existing arrange blocks in `RecurringJobSeederTests.cs`. `RecurringJobDiscoveryService`'s four-argument constructor and `HangfireOptions { SchedulerEnabled = true }` in the new tests match the two existing usages in `HangfireRecurringJobSchedulerTests.cs:55-59` and `:81-85`. The literals `"Europe/Prague"` (= `ParityTestRecurringJob.Metadata.TimeZoneId` and `CreateTestJob`'s `timeZoneId:`) and `"America/New_York"` (= the `invoice-classification` mock job's metadata zone) were each read from the source files rather than assumed.
