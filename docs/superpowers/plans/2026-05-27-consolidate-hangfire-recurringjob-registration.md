# Consolidate Hangfire RecurringJob Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace two duplicated reflection-based Hangfire registration paths with a single shared helper that standardises on the `RecurringJobOptions` overload.

**Architecture:** Add `HangfireJobRegistrationHelper` (public static) in `Anela.Heblo.Application/Features/BackgroundJobs/Services/`. Both `RecurringJobDiscoveryService` (startup) and `HangfireRecurringJobScheduler` (runtime updates) delegate to it. Helper validates inputs, resolves a private generic dispatcher via reflection, unwraps `TargetInvocationException`, and invokes `RecurringJob.AddOrUpdate<TJob>(..., RecurringJobOptions)` so both call sites produce identical Hangfire records.

**Tech Stack:** .NET 8, C#, Hangfire.Core 1.8.21, xUnit, Hangfire.MemoryStorage (tests).

> **Layer placement note:** The original spec FR-6 proposed placing the helper in `Anela.Heblo.API/Infrastructure/Hangfire/`, but the architecture review (Amendment 1) corrected this — `Anela.Heblo.Application` cannot reference `Anela.Heblo.API` (would be circular). Application already references `Hangfire.Core`, so the helper lives in Application and is consumed by API via the existing `API → Application` reference.

---

## File Structure

**New files:**
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — shared helper (public static)
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs` — helper unit tests
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` — scheduler + parity tests

**Modified files:**
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — remove reflection dispatch + private generic method, call helper instead
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` — remove reflection dispatch + private generic method, call helper instead

**Unchanged (verified, no edits required):**
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJob.cs`
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobMetadata.cs`
- `BackgroundJobsModule.cs`, `ServiceCollectionExtensions.cs` — no DI changes
- All `csproj` files — no new references

---

## Task 1: Add helper unit tests (TDD red)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

[Collection("Hangfire")]
public class HangfireJobRegistrationHelperTests : IDisposable
{
    public HangfireJobRegistrationHelperTests(HangfireTestFixture fixture)
    {
        // Hangfire is initialized by the shared collection fixture.
    }

    [Fact]
    public void RegisterOrUpdate_WithValidInputs_RegistersJobInHangfireStorage()
    {
        // Arrange
        const string jobName = "helper-test-job";
        const string cron = "0 5 * * *";
        const string tz = "Europe/Prague";

        // Act
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob),
            jobName,
            cron,
            tz);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = connection.GetRecurringJobs().SingleOrDefault(j => j.Id == jobName);
        Assert.NotNull(job);
        Assert.Equal(cron, job.Cron);
        Assert.Equal(tz, job.TimeZoneId);
        // The helper must register the async overload (returns Task)
        Assert.Equal(typeof(Task), job.Job.Method.ReturnType);
    }

    [Fact]
    public void RegisterOrUpdate_CalledTwice_UpdatesCronOnSecondCall()
    {
        // Arrange
        const string jobName = "helper-test-update-job";
        const string firstCron = "0 1 * * *";
        const string secondCron = "0 2 * * *";

        // Act — register, then re-register with new CRON
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob), jobName, firstCron, "Europe/Prague");
        HangfireJobRegistrationHelper.RegisterOrUpdate(
            typeof(HelperTestRecurringJob), jobName, secondCron, "Europe/Prague");

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == jobName);
        Assert.Equal(secondCron, job.Cron);
    }

    [Fact]
    public void RegisterOrUpdate_WithNullJobType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                null!, "name", "0 0 * * *", "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingJobName_ThrowsArgumentException(string? jobName)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), jobName!, "0 0 * * *", "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingCron_ThrowsArgumentException(string? cron)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), "name", cron!, "Europe/Prague"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterOrUpdate_WithMissingTimeZoneId_ThrowsArgumentException(string? tz)
    {
        Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob), "name", "0 0 * * *", tz!));
    }

    [Fact]
    public void RegisterOrUpdate_WithTypeNotImplementingIRecurringJob_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(NotARecurringJob), "name", "0 0 * * *", "Europe/Prague"));
        Assert.Equal("jobType", ex.ParamName);
    }

    [Fact]
    public void RegisterOrUpdate_WithInvalidTimeZoneId_ThrowsUnwrappedTimeZoneNotFoundException()
    {
        // The helper must unwrap TargetInvocationException so callers see the
        // real cause (TimeZoneNotFoundException), not the reflection wrapper.
        Assert.Throws<TimeZoneNotFoundException>(() =>
            HangfireJobRegistrationHelper.RegisterOrUpdate(
                typeof(HelperTestRecurringJob),
                "helper-test-tz-job",
                "0 0 * * *",
                "Not/A/Real/TimeZone"));
    }

    public void Dispose()
    {
        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            RecurringJob.RemoveIfExists(job.Id);
        }
    }

    private class HelperTestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; } = new()
        {
            JobName = "helper-test-job",
            DisplayName = "Helper Test Job",
            Description = "Used by HangfireJobRegistrationHelperTests",
            CronExpression = "0 0 * * *",
            TimeZoneId = "Europe/Prague",
        };

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class NotARecurringJob
    {
        // Used to assert the helper rejects types that do not implement IRecurringJob.
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (helper does not exist yet)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~HangfireJobRegistrationHelperTests" \
    --no-restore --nologo
```

Expected: Build error — `The type or namespace name 'HangfireJobRegistrationHelper' could not be found`. This is the RED state; the tests cannot even compile yet.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobRegistrationHelperTests.cs
git commit -m "test: add HangfireJobRegistrationHelper unit tests (red)"
```

---

## Task 2: Create the helper (TDD green)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`

- [ ] **Step 1: Write the helper**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`:

```csharp
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Single entry point for binding a runtime <see cref="Type"/> to
/// <see cref="RecurringJob.AddOrUpdate{TJob}(string, System.Linq.Expressions.Expression{Action{TJob}}, string, RecurringJobOptions)"/>.
/// Used by both startup discovery and runtime CRON updates so that both code paths
/// produce identical Hangfire <c>RecurringJob</c> records.
/// </summary>
public static class HangfireJobRegistrationHelper
{
    /// <summary>
    /// Registers or updates a Hangfire recurring job for the given runtime job type.
    /// Always uses the <see cref="RecurringJobOptions"/> overload.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="jobType"/> is null.</exception>
    /// <exception cref="ArgumentException">A string argument is null/empty/whitespace, or <paramref name="jobType"/> does not implement <see cref="IRecurringJob"/>.</exception>
    /// <exception cref="TimeZoneNotFoundException">The time zone id is not resolvable on this host.</exception>
    public static void RegisterOrUpdate(
        Type jobType,
        string jobName,
        string cronExpression,
        string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(jobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (!typeof(IRecurringJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException(
                $"{jobType.FullName} does not implement {nameof(IRecurringJob)}.",
                nameof(jobType));
        }

        var dispatcher = typeof(HangfireJobRegistrationHelper)
            .GetMethod(nameof(RegisterOrUpdateGeneric), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Could not resolve {nameof(RegisterOrUpdateGeneric)} via reflection.");

        var closed = dispatcher.MakeGenericMethod(jobType);

        try
        {
            closed.Invoke(null, new object[] { jobName, cronExpression, timeZoneId });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface the real cause (e.g. TimeZoneNotFoundException) instead of the
            // reflection wrapper, so callers can log structured context.
            throw ex.InnerException;
        }
    }

    private static void RegisterOrUpdateGeneric<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId)
        where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
            });
    }
}
```

- [ ] **Step 2: Run the helper tests to verify they pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~HangfireJobRegistrationHelperTests" \
    --no-restore --nologo
```

Expected: All 11 tests (`Theory` rows count separately) pass.

- [ ] **Step 3: Commit the helper**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs
git commit -m "feat(background-jobs): add HangfireJobRegistrationHelper"
```

---

## Task 3: Refactor `RecurringJobDiscoveryService` to use the helper

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`

- [ ] **Step 1: Replace reflection dispatch with helper call**

In `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`:

Replace the entire block from line 79 to line 96 (the `var registerMethod = ...` block, the null-check, the `MakeGenericMethod` call, and the `Invoke`) with a single helper call:

```csharp
                    HangfireJobRegistrationHelper.RegisterOrUpdate(
                        jobType,
                        metadata.JobName,
                        cronExpression,
                        metadata.TimeZoneId);
```

The surrounding `try/catch` (lines 67 outer and 102–107) **stays as-is** — the catch already logs structured context for `metadata.JobName` and `jobType.Name`.

- [ ] **Step 2: Delete the now-unused private generic method**

Delete the entire `RegisterRecurringJobInternal<TJob>` method (lines 129–142):

```csharp
    private static void RegisterRecurringJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
            });
    }
```

- [ ] **Step 3: Add the helper using and remove unused usings**

At the top of the file, add:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
```

Verify these `using` statements remain (they are still used elsewhere in the file):
- `using Anela.Heblo.Domain.Features.BackgroundJobs;` — for `IRecurringJob`, `IRecurringJobConfigurationRepository`
- `using Anela.Heblo.Xcc;` — for `HangfireOptions`
- `using Hangfire;` — *no longer used* once `RegisterRecurringJobInternal` is gone. Remove it.
- `using Microsoft.Extensions.Options;` — for `IOptions<HangfireOptions>`

The final top-of-file usings should be:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Xcc;
using Microsoft.Extensions.Options;
```

- [ ] **Step 4: Build to verify the API project compiles**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: Build succeeds with zero errors. Warnings should not increase versus baseline.

- [ ] **Step 5: Run the existing `RecurringJobDiscoveryServiceTests` to verify behaviour parity**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~RecurringJobDiscoveryServiceTests" \
    --no-restore --nologo
```

Expected: All three existing tests pass (`StartAsync_WithSchedulerEnabled_RegistersRecurringJobs`, `StartAsync_WhenDbConfigExists_UsesDbCronInsteadOfMetadataDefault`, `StartAsync_WithSchedulerDisabled_DoesNotRegisterJobs`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs
git commit -m "refactor(background-jobs): route RecurringJobDiscoveryService through HangfireJobRegistrationHelper"
```

---

## Task 4: Refactor `HangfireRecurringJobScheduler` to use the helper

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs`

- [ ] **Step 1: Replace reflection dispatch with helper call**

Open `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs`.

Replace the block from line 41 to line 64 (everything from `var jobType = job.GetType();` through the `try { genericMethod.Invoke(...) } catch (...) { ... return; }` block) with this simpler implementation:

```csharp
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
```

The trailing success log (lines 66–68) stays as-is.

- [ ] **Step 2: Delete the now-unused private generic method**

Delete the entire `UpdateJobInternal<TJob>` method (lines 71–81):

```csharp
    private static void UpdateJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            j => j.ExecuteAsync(default),
            cronExpression,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }
```

- [ ] **Step 3: Remove unused usings**

At the top of the file, remove these `using` lines — they were used only by the deleted reflection block and the deleted private method:

```csharp
using System.Reflection;
using Hangfire;
```

Verify these `using` statements remain (still used):
- `using Anela.Heblo.Domain.Features.BackgroundJobs;` — for `IRecurringJob`
- `using Microsoft.Extensions.DependencyInjection;` — for `CreateScope`, `GetServices`
- `using Microsoft.Extensions.Logging;` — for `ILogger<>`

The final top-of-file usings should be:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

(No `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` is needed — the helper is in the same namespace.)

- [ ] **Step 4: Update the class-level docstring**

Update the XML doc comment on the `HangfireRecurringJobScheduler` class (lines 9–12) from:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live using the same
/// reflection pattern as RecurringJobDiscoveryService.
/// </summary>
```

to:

```csharp
/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live by delegating to
/// <see cref="HangfireJobRegistrationHelper"/> so the runtime-update path uses
/// the same registration code as startup discovery.
/// </summary>
```

- [ ] **Step 5: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: Build succeeds with zero errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs
git commit -m "refactor(background-jobs): route HangfireRecurringJobScheduler through HangfireJobRegistrationHelper"
```

---

## Task 5: Add behaviour-parity test for the scheduler

This task creates a new test file that exercises `HangfireRecurringJobScheduler` against in-memory Hangfire storage and asserts that:
1. The scheduler successfully updates the CRON of a previously-registered job.
2. The resulting Hangfire record after a scheduler update is structurally identical (CRON, time zone, async return type) to a record produced by `RecurringJobDiscoveryService` — the behaviour-parity check from arch-review Amendment 4.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`:

```csharp
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Anela.Heblo.Xcc;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

[Collection("Hangfire")]
public class HangfireRecurringJobSchedulerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public HangfireRecurringJobSchedulerTests(HangfireTestFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = "Test" });
        services.AddScoped<IRecurringJob, ParityTestRecurringJob>();
        services.AddScoped<ParityTestRecurringJob>();
        services.AddSingleton<IRecurringJobConfigurationRepository, EmptyStubRepository>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void UpdateCronSchedule_WithUnknownJobName_LogsWarningAndReturns()
    {
        // Arrange
        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *");

        // Assert — no job was created
        using var connection = JobStorage.Current.GetConnection();
        Assert.Empty(connection.GetRecurringJobs().Where(j => j.Id == "does-not-exist"));
    }

    [Fact]
    public async Task UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage()
    {
        // Arrange — first register via discovery service (startup path)
        const string newCron = "0 4 * * 2"; // differs from metadata default
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

        // Act — update via the runtime scheduler path
        scheduler.UpdateCronSchedule("parity-test-job", newCron);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");
        Assert.Equal(newCron, job.Cron);
        Assert.Equal("Europe/Prague", job.TimeZoneId);
    }

    [Fact]
    public async Task UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration()
    {
        // Arrange — register two jobs: one via the discovery path, one re-registered
        // via the scheduler path. Both should end up with the same async method signature
        // and same time-zone metadata. This is the behaviour-parity check (arch-review Amendment 4).
        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var discovery = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions);
        await discovery.StartAsync(CancellationToken.None);

        using (var connection = JobStorage.Current.GetConnection())
        {
            var discovered = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");

            // Act — re-register through the scheduler with a new CRON
            var scheduler = new HangfireRecurringJobScheduler(
                _serviceProvider,
                _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());
            scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *");

            // Assert — re-fetch and compare structural metadata
            var afterUpdate = Assert.Single(
                JobStorage.Current.GetConnection().GetRecurringJobs(),
                j => j.Id == "parity-test-job");

            Assert.Equal(discovered.Job.Method.ReturnType, afterUpdate.Job.Method.ReturnType);
            Assert.Equal(typeof(Task), afterUpdate.Job.Method.ReturnType);
            Assert.Equal(discovered.TimeZoneId, afterUpdate.TimeZoneId);
            Assert.Equal(discovered.Job.Type, afterUpdate.Job.Type);
            Assert.Equal(discovered.Job.Method.Name, afterUpdate.Job.Method.Name);
        }
    }

    public void Dispose()
    {
        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            RecurringJob.RemoveIfExists(job.Id);
        }
        _serviceProvider?.Dispose();
    }

    private class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = "/test";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "/test/wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private class ParityTestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; } = new()
        {
            JobName = "parity-test-job",
            DisplayName = "Parity Test Job",
            Description = "Used by HangfireRecurringJobSchedulerTests",
            CronExpression = "0 0 * * *",
            TimeZoneId = "Europe/Prague",
        };

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class EmptyStubRepository : IRecurringJobConfigurationRepository
    {
        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RecurringJobConfiguration>());

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run the new scheduler tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~HangfireRecurringJobSchedulerTests" \
    --no-restore --nologo
```

Expected: All 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs
git commit -m "test: add HangfireRecurringJobScheduler parity tests"
```

---

## Task 6: Full validation and final commit

This task is the final sanity check: run the full BackgroundJobs test suite, the full backend build, and `dotnet format` to satisfy `CLAUDE.md`'s validation gate.

- [ ] **Step 1: Run the entire BackgroundJobs test folder**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Features.BackgroundJobs" \
    --no-restore --nologo
```

Expected: All BackgroundJobs tests pass — no regression in the existing `RecurringJobDiscoveryServiceTests`, `UpdateRecurringJobCronHandlerTests`, `TriggerRecurringJobHandlerIntegrationTests`, `HangfireJobEnqueuerTests`, etc.

- [ ] **Step 2: Build the full backend solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: Build succeeds with zero errors. Warning count should not increase versus the baseline before this branch.

- [ ] **Step 3: Run `dotnet format` (CLAUDE.md gate)**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```

If any files are reformatted, stage and commit them:

```bash
git add -u
git diff --cached --quiet || git commit -m "chore: dotnet format"
```

- [ ] **Step 4: Confirm no leftover references to the old internal methods**

Search for any orphaned references:

```bash
grep -rn "RegisterRecurringJobInternal\|UpdateJobInternal" backend/src backend/test
```

Expected output: empty (no matches). If anything appears, it is dead code from a missed deletion — remove it and amend the relevant prior commit or add a fix commit.

- [ ] **Step 5: Confirm exactly one `RecurringJob.AddOrUpdate` call site remains (FR-2 acceptance)**

Run:
```bash
grep -rn "RecurringJob.AddOrUpdate" backend/src
```

Expected output: a single match inside `HangfireJobRegistrationHelper.cs` (the `RegisterOrUpdateGeneric<TJob>` body).

Also confirm the legacy `TimeZoneInfo`-only overload is gone:

```bash
grep -rn "AddOrUpdate<.*>(.*TimeZoneInfo\." backend/src
```

Expected output: empty.

- [ ] **Step 6: Final commit if anything was tweaked in Step 5**

If any additional changes were required, commit them:

```bash
git add -u
git diff --cached --quiet || git commit -m "chore(background-jobs): tidy post-consolidation"
```

If no changes were needed, this step is a no-op.

---

## Acceptance Criteria Mapping

| Spec/Arch Requirement | Implemented in |
|---|---|
| FR-1: Single helper with reflection plumbing + input validation | Task 1 (tests), Task 2 (impl) |
| FR-2: Only one `RecurringJob.AddOrUpdate<TJob>` call site, `RecurringJobOptions` overload | Task 2 (helper), Task 6 Step 5 (verification) |
| FR-3: `RecurringJobDiscoveryService` delegates to helper, no more reflection there | Task 3 |
| FR-4: `HangfireRecurringJobScheduler` delegates to helper, no more reflection there | Task 4 |
| FR-5: Behaviour parity (same names/CRON/TZ; runtime updates work) | Task 3 Step 5 (existing tests), Task 5 (parity test) |
| FR-6 (corrected by Amendment 1): Helper in `Application/Features/BackgroundJobs/Services/`, `public static`, no new project references | Task 2 |
| Amendment 2: `public static` visibility | Task 2 |
| Amendment 3: Unwrap `TargetInvocationException` | Task 1 (test), Task 2 (impl) |
| Amendment 4: Behaviour-parity test | Task 5 |
| Amendment 5: Helper unit tests | Task 1 |
| NFR-3: Single place for future Hangfire options changes | Task 2 |
| NFR-4: Helper unit-testable in isolation | Task 1 |
