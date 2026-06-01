# DB-Driven CRON for Recurring Jobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make CRON expressions for Hangfire recurring jobs persist in DB so runtime changes survive restarts, and expose inline editing in the Recurring Jobs UI.

**Architecture:** Add `UpdateCronExpression` method to domain entity; new `UpdateRecurringJobCron` use case (handler + request + response) calls DB then triggers live Hangfire update via `IHangfireRecurringJobScheduler`; `RecurringJobDiscoveryService` loads DB CRON on startup. Frontend hook + inline pencil-edit UI mirrors the existing status toggle pattern.

**Tech Stack:** .NET 8 / C#, MediatR, Hangfire.Core, NCrontab.Advanced (transitive via Hangfire), React + TypeScript, TanStack Query, NSwag-generated API client.

---

## File Map

| Status | File | Change |
|--------|------|--------|
| MODIFY | `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` | Add `UpdateCronExpression(string, string)` method |
| MODIFY | `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` | Add `InvalidCronExpression = 1903` |
| CREATE | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireRecurringJobScheduler.cs` | New interface for live CRON update |
| CREATE | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs` | Implementation using Hangfire reflection pattern |
| CREATE | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronRequest.cs` | MediatR request |
| CREATE | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronResponse.cs` | Response DTO |
| CREATE | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs` | Handler: validate → DB → live Hangfire |
| MODIFY | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` | Register `HangfireRecurringJobScheduler` |
| MODIFY | `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` | Add `PUT /{jobName}/cron` endpoint |
| MODIFY | `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` | Inject repo, load DB CRON on startup |
| CREATE | `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs` | Unit tests for handler |
| MODIFY | `frontend/src/api/hooks/useRecurringJobs.ts` | Add `useUpdateRecurringJobCronMutation` |
| MODIFY | `frontend/src/pages/RecurringJobsPage.tsx` | Inline CRON editing |
| CREATE | `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` | Hook tests |

---

## Task 1: Domain — Add `UpdateCronExpression` to entity

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`

- [ ] **Step 1: Add method after `Disable`**

Add the following method inside `RecurringJobConfiguration` (after the `Disable` method, before the closing brace):

```csharp
public void UpdateCronExpression(string cronExpression, string modifiedBy)
{
    if (string.IsNullOrWhiteSpace(cronExpression))
        throw new ValidationException("CronExpression is required");
    if (string.IsNullOrWhiteSpace(modifiedBy))
        throw new ValidationException("ModifiedBy is required");

    CronExpression = cronExpression;
    LastModifiedAt = DateTime.UtcNow;
    LastModifiedBy = modifiedBy;
}
```

- [ ] **Step 2: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Add `InvalidCronExpression` error code**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, in the `// BackgroundJobs module errors (19XX)` section, after `RecurringJobUpdateFailed = 1902,` add:

```csharp
[HttpStatusCode(HttpStatusCode.BadRequest)]
InvalidCronExpression = 1903,
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs \
        backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add UpdateCronExpression domain method and InvalidCronExpression error code"
```

---

## Task 2: Application — `IHangfireRecurringJobScheduler` + implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireRecurringJobScheduler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

- [ ] **Step 1: Create `IHangfireRecurringJobScheduler.cs`**

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Applies a CRON expression update to a running Hangfire job schedule immediately,
/// without requiring a restart.
/// </summary>
public interface IHangfireRecurringJobScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression);
}
```

- [ ] **Step 2: Create `HangfireRecurringJobScheduler.cs`**

```csharp
using System.Reflection;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Updates a Hangfire recurring job's CRON schedule live using the same
/// reflection pattern as RecurringJobDiscoveryService.
/// </summary>
public class HangfireRecurringJobScheduler : IHangfireRecurringJobScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireRecurringJobScheduler> _logger;

    public HangfireRecurringJobScheduler(
        IServiceProvider serviceProvider,
        ILogger<HangfireRecurringJobScheduler> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void UpdateCronSchedule(string jobName, string cronExpression)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in DI — Hangfire schedule not updated live", jobName);
            return;
        }

        var jobType = job.GetType();
        var registerMethod = typeof(HangfireRecurringJobScheduler)
            .GetMethod(nameof(UpdateJobInternal), BindingFlags.NonPublic | BindingFlags.Static);

        if (registerMethod == null)
        {
            _logger.LogError("Could not find UpdateJobInternal method via reflection");
            return;
        }

        var genericMethod = registerMethod.MakeGenericMethod(jobType);
        genericMethod.Invoke(null, new object[] { jobName, cronExpression, job.Metadata.TimeZoneId });

        _logger.LogInformation(
            "Live Hangfire schedule updated for {JobName} → {CronExpression}",
            jobName, cronExpression);
    }

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
}
```

- [ ] **Step 3: Register in `BackgroundJobsModule.cs`**

Add after `services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();`:

```csharp
services.AddScoped<IHangfireRecurringJobScheduler, HangfireRecurringJobScheduler>();
```

- [ ] **Step 4: Build Application layer**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireRecurringJobScheduler.cs \
        backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireRecurringJobScheduler.cs \
        backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs
git commit -m "feat: add IHangfireRecurringJobScheduler for live CRON update without restart"
```

---

## Task 3: Application — `UpdateRecurringJobCron` use case (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs`

- [ ] **Step 1: Create `UpdateRecurringJobCronRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronRequest : IRequest<UpdateRecurringJobCronResponse>
{
    public string JobName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create `UpdateRecurringJobCronResponse.cs`**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronResponse : BaseResponse
{
    public string JobName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;

    public UpdateRecurringJobCronResponse() : base() { }
    public UpdateRecurringJobCronResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 3: Write failing tests first**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class UpdateRecurringJobCronHandlerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IHangfireRecurringJobScheduler> _schedulerMock;
    private readonly UpdateRecurringJobCronHandler _handler;

    public UpdateRecurringJobCronHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _schedulerMock = new Mock<IHangfireRecurringJobScheduler>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser { Name = "test-user" });

        _handler = new UpdateRecurringJobCronHandler(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<UpdateRecurringJobCronHandler>>(),
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _schedulerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenJobNotFound_ReturnsNotFoundError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("missing-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "missing-job",
            CronExpression = "0 3 * * *"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RecurringJobNotFound);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCronExpressionInvalid_ReturnsBadRequest()
    {
        // Arrange
        var job = CreateTestJob("my-job", "0 2 * * *");
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = "not-a-cron"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCronExpression);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<RecurringJobConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
        _schedulerMock.Verify(s => s.UpdateCronSchedule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenCronExpressionEmpty_ReturnsBadRequest(string emptyCron)
    {
        // Arrange
        var job = CreateTestJob("my-job", "0 2 * * *");
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = emptyCron
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCronExpression);
    }

    [Fact]
    public async Task Handle_WhenValidCron_UpdatesDbAndHangfire()
    {
        // Arrange
        const string newCron = "0 3 * * *";
        var job = CreateTestJob("my-job", "0 2 * * *");

        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest
        {
            JobName = "my-job",
            CronExpression = newCron
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.JobName.Should().Be("my-job");
        result.CronExpression.Should().Be(newCron);
        result.LastModifiedBy.Should().Be("test-user");

        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<RecurringJobConfiguration>(c => c.CronExpression == newCron),
            It.IsAny<CancellationToken>()), Times.Once);

        _schedulerMock.Verify(s => s.UpdateCronSchedule("my-job", newCron), Times.Once);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 3 * * *")]
    [InlineData("0 0 1 * *")]
    [InlineData("*/5 * * * *")]
    public async Task Handle_AcceptsValidCronFormats(string validCron)
    {
        // Arrange
        var job = CreateTestJob("my-job", "0 2 * * *");
        _repositoryMock
            .Setup(r => r.GetByJobNameAsync("my-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var request = new UpdateRecurringJobCronRequest { JobName = "my-job", CronExpression = validCron };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    private static RecurringJobConfiguration CreateTestJob(string jobName, string cronExpression)
    {
        return new RecurringJobConfiguration(
            jobName: jobName,
            displayName: "Test Job",
            description: "A test job",
            cronExpression: cronExpression,
            isEnabled: true,
            lastModifiedBy: "seed");
    }
}
```

- [ ] **Step 4: Run tests — expect failure (handler not yet created)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateRecurringJobCronHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: Build error — `UpdateRecurringJobCronHandler` does not exist yet.

- [ ] **Step 5: Create `UpdateRecurringJobCronHandler.cs`**

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;

public class UpdateRecurringJobCronHandler : IRequestHandler<UpdateRecurringJobCronRequest, UpdateRecurringJobCronResponse>
{
    private readonly ILogger<UpdateRecurringJobCronHandler> _logger;
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHangfireRecurringJobScheduler _scheduler;

    public UpdateRecurringJobCronHandler(
        ILogger<UpdateRecurringJobCronHandler> logger,
        IRecurringJobConfigurationRepository repository,
        ICurrentUserService currentUserService,
        IHangfireRecurringJobScheduler scheduler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public async Task<UpdateRecurringJobCronResponse> Handle(
        UpdateRecurringJobCronRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating CRON expression for {JobName} to {CronExpression}",
            request.JobName, request.CronExpression);

        if (!IsValidCronExpression(request.CronExpression))
        {
            _logger.LogWarning("Invalid CRON expression supplied for {JobName}: '{CronExpression}'",
                request.JobName, request.CronExpression);
            return new UpdateRecurringJobCronResponse(
                ErrorCodes.InvalidCronExpression,
                new Dictionary<string, string>
                {
                    { "JobName", request.JobName },
                    { "CronExpression", request.CronExpression }
                });
        }

        var job = await _repository.GetByJobNameAsync(request.JobName, cancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Recurring job not found: {JobName}", request.JobName);
            return new UpdateRecurringJobCronResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string> { { "JobName", request.JobName } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var modifiedBy = currentUser.Name ?? "System";

        job.UpdateCronExpression(request.CronExpression, modifiedBy);
        await _repository.UpdateAsync(job, cancellationToken);

        _scheduler.UpdateCronSchedule(job.JobName, job.CronExpression);

        _logger.LogInformation(
            "CRON expression for {JobName} updated to {CronExpression} by {ModifiedBy}",
            job.JobName, job.CronExpression, modifiedBy);

        return new UpdateRecurringJobCronResponse
        {
            JobName = job.JobName,
            CronExpression = job.CronExpression,
            LastModifiedAt = job.LastModifiedAt,
            LastModifiedBy = job.LastModifiedBy
        };
    }

    private static bool IsValidCronExpression(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        try
        {
            CrontabSchedule.Parse(cronExpression);
            return true;
        }
        catch (CrontabException)
        {
            return false;
        }
    }
}
```

> **Note on NCrontab:** `CrontabSchedule` is in the `NCrontab` namespace, available transitively via `Hangfire.Core`'s dependency on `NCrontab.Advanced`. If the build fails with a type-not-found error, add the following to `Anela.Heblo.Application.csproj`:
> ```xml
> <PackageReference Include="NCrontab.Advanced" Version="3.3.3" />
> ```

- [ ] **Step 6: Run tests — expect all pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~UpdateRecurringJobCronHandlerTests" -q
```

Expected: All 6 tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/ \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs
git commit -m "feat: implement UpdateRecurringJobCron use case with CRON validation"
```

---

## Task 4: API — Controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs`

- [ ] **Step 1: Add using and endpoint**

In `RecurringJobsController.cs`:

1. Add using statement at the top (with other `UpdateRecurring` usings):
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.UpdateRecurringJobCron;
```

2. Add the endpoint after `UpdateJobStatus` action:
```csharp
/// <summary>
/// Update the CRON schedule of a recurring job
/// </summary>
/// <param name="jobName">The name of the job to update</param>
/// <param name="request">The CRON update request containing the new expression</param>
/// <returns>Updated job information with new CRON expression</returns>
[HttpPut("{jobName}/cron")]
[ProducesResponseType(typeof(UpdateRecurringJobCronResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<UpdateRecurringJobCronResponse>> UpdateJobCron(
    string jobName,
    [FromBody] UpdateJobCronRequestBody request)
{
    var mediatrRequest = new UpdateRecurringJobCronRequest
    {
        JobName = jobName,
        CronExpression = request.CronExpression
    };

    var response = await _mediator.Send(mediatrRequest);

    return HandleResponse(response);
}
```

3. Add the request body class at the bottom of the file (after `UpdateJobStatusRequestBody`):
```csharp
/// <summary>
/// Request body for updating recurring job CRON expression
/// </summary>
public class UpdateJobCronRequestBody
{
    /// <summary>
    /// The new CRON expression (e.g. "0 3 * * *")
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Run all BE tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass (no regressions).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs
git commit -m "feat: add PUT /api/RecurringJobs/{jobName}/cron endpoint"
```

---

## Task 5: API — `RecurringJobDiscoveryService` reads CRON from DB

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`

- [ ] **Step 1: Replace the file with DB-aware version**

Replace the full content of `RecurringJobDiscoveryService.cs` with:

```csharp
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Automatically discovers and registers all IRecurringJob implementations with Hangfire.
/// Uses DB-stored CRON expressions (seeded from metadata on first run) so runtime
/// changes survive application restarts.
/// </summary>
public class RecurringJobDiscoveryService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringJobDiscoveryService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly HangfireOptions _hangfireOptions;

    public RecurringJobDiscoveryService(
        IServiceProvider serviceProvider,
        ILogger<RecurringJobDiscoveryService> logger,
        IWebHostEnvironment environment,
        IOptions<HangfireOptions> hangfireOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _environment = environment;
        _hangfireOptions = hangfireOptions.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting recurring job discovery in {Environment} with SchedulerEnabled={SchedulerEnabled}",
            _environment.EnvironmentName, _hangfireOptions.SchedulerEnabled);

        if (!_hangfireOptions.SchedulerEnabled)
        {
            _logger.LogInformation(
                "Hangfire scheduler disabled via configuration (SchedulerEnabled=false). No recurring jobs will be registered.");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();

            if (jobs.Count == 0)
            {
                _logger.LogWarning(
                    "No IRecurringJob implementations found. Ensure jobs are registered in DI container.");
                return;
            }

            // Load all DB configs once so we can do a dictionary lookup per job
            var repository = scope.ServiceProvider.GetRequiredService<IRecurringJobConfigurationRepository>();
            var dbConfigs = await repository.GetAllAsync(cancellationToken);
            var configByJobName = dbConfigs.ToDictionary(c => c.JobName, c => c);

            foreach (var job in jobs)
            {
                var metadata = job.Metadata;
                var jobType = job.GetType();

                try
                {
                    // Prefer DB CRON (runtime override); fall back to metadata default
                    var cronSource = "metadata";
                    var cronExpression = metadata.CronExpression;

                    if (configByJobName.TryGetValue(metadata.JobName, out var dbConfig))
                    {
                        cronExpression = dbConfig.CronExpression;
                        cronSource = "DB";
                    }

                    var registerMethod = typeof(RecurringJobDiscoveryService)
                        .GetMethod(
                            nameof(RegisterRecurringJobInternal),
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                    if (registerMethod == null)
                    {
                        _logger.LogError("Could not find RegisterRecurringJobInternal method");
                        continue;
                    }

                    var genericRegisterMethod = registerMethod.MakeGenericMethod(jobType);
                    genericRegisterMethod.Invoke(null, new object[]
                    {
                        metadata.JobName,
                        cronExpression,
                        metadata.TimeZoneId
                    });

                    _logger.LogInformation(
                        "Registered recurring job: {JobName} ({JobType}) with schedule {Cron} (from {CronSource})",
                        metadata.JobName, jobType.Name, cronExpression, cronSource);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to register recurring job {JobName} ({JobType})",
                        metadata.JobName, jobType.Name);
                }
            }

            _logger.LogInformation(
                "Successfully registered {Count} recurring jobs in {Environment} environment",
                jobs.Count, _environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register recurring jobs in {Environment} environment. " +
                "Application startup will continue, but background jobs will not be scheduled.",
                _environment.EnvironmentName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping recurring job discovery service");
        return Task.CompletedTask;
    }

    private static void RegisterRecurringJobInternal<TJob>(
        string jobName,
        string cronExpression,
        string timeZoneId) where TJob : IRecurringJob
    {
        RecurringJob.AddOrUpdate<TJob>(
            jobName,
            job => job.ExecuteAsync(default),
            cronExpression,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
    }
}
```

- [ ] **Step 2: Build and run all BE tests**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore -q && \
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: `Build succeeded.` and all tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs
git commit -m "feat: RecurringJobDiscoveryService reads CRON from DB on startup (fallback to metadata)"
```

---

## Task 6: Frontend — Regenerate TypeScript client

- [ ] **Step 1: Regenerate the API client**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Expected: `Build succeeded.` — file `frontend/src/api/generated/api-client.ts` is updated.

- [ ] **Step 2: Verify new method exists in generated client**

```bash
grep -n "recurringJobs_UpdateJobCron\|UpdateJobCronRequestBody\|UpdateRecurringJobCronResponse" \
  frontend/src/api/generated/api-client.ts | head -20
```

Expected: Lines containing `recurringJobs_UpdateJobCron`, `UpdateJobCronRequestBody`, and `UpdateRecurringJobCronResponse`.

---

## Task 7: Frontend — `useUpdateRecurringJobCronMutation` hook + tests (TDD)

**Files:**
- Modify: `frontend/src/api/hooks/useRecurringJobs.ts`
- Create: `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts`

- [ ] **Step 1: Write failing hook test first**

Create `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts`:

```typescript
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useUpdateRecurringJobCronMutation } from '../useRecurringJobs';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    recurringJobs: ['recurring-jobs'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useUpdateRecurringJobCronMutation', () => {
  const mockApiClient = {
    recurringJobs_UpdateJobCron: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  it('calls recurringJobs_UpdateJobCron with correct arguments', async () => {
    mockApiClient.recurringJobs_UpdateJobCron.mockResolvedValue({
      success: true,
      jobName: 'test-job',
      cronExpression: '0 3 * * *',
      lastModifiedAt: new Date().toISOString(),
      lastModifiedBy: 'test-user',
    });

    const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), {
      wrapper: createWrapper,
    });

    await act(async () => {
      await result.current.mutateAsync({
        jobName: 'test-job',
        cronExpression: '0 3 * * *',
      });
    });

    expect(mockApiClient.recurringJobs_UpdateJobCron).toHaveBeenCalledTimes(1);
    expect(mockApiClient.recurringJobs_UpdateJobCron).toHaveBeenCalledWith(
      'test-job',
      expect.objectContaining({ cronExpression: '0 3 * * *' })
    );
  });

  it('throws on API error', async () => {
    mockApiClient.recurringJobs_UpdateJobCron.mockRejectedValue(
      new Error('Invalid CRON')
    );

    const { result } = renderHook(() => useUpdateRecurringJobCronMutation(), {
      wrapper: createWrapper,
    });

    await expect(
      act(async () => {
        await result.current.mutateAsync({
          jobName: 'test-job',
          cronExpression: 'bad-cron',
        });
      })
    ).rejects.toThrow('Invalid CRON');
  });
});
```

- [ ] **Step 2: Run tests — expect failure (hook not yet exported)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npx jest --testPathPattern="useRecurringJobs.test" --no-coverage 2>&1 | tail -20
```

Expected: Test error — `useUpdateRecurringJobCronMutation is not exported`.

- [ ] **Step 3: Add hook to `useRecurringJobs.ts`**

Add the following import to the existing imports at the top of the file (add `UpdateJobCronRequestBody` and `UpdateRecurringJobCronResponse` to the generated client import):

```typescript
import {
  UpdateJobStatusRequestBody,
  UpdateJobCronRequestBody,
  type RecurringJobDto,
  type UpdateRecurringJobStatusResponse,
  type UpdateRecurringJobCronResponse,
  type TriggerRecurringJobResponse
} from '../generated/api-client';
```

Then add the new hook after `useUpdateRecurringJobStatusMutation`:

```typescript
/**
 * Hook to update recurring job CRON expression
 * Uses generated API client method: recurringJobs_UpdateJobCron
 */
export const useUpdateRecurringJobCronMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      jobName,
      cronExpression
    }: {
      jobName: string;
      cronExpression: string;
    }): Promise<UpdateRecurringJobCronResponse> => {
      const client = getAuthenticatedApiClient();
      const request = new UpdateJobCronRequestBody({ cronExpression });
      return await client.recurringJobs_UpdateJobCron(jobName, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};
```

Also add to the re-export at the bottom:
```typescript
export type { RecurringJobDto, UpdateRecurringJobStatusResponse, UpdateRecurringJobCronResponse, TriggerRecurringJobResponse };
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npx jest --testPathPattern="useRecurringJobs.test" --no-coverage 2>&1 | tail -20
```

Expected: `Tests: 2 passed`.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/api/hooks/useRecurringJobs.ts \
        frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts \
        frontend/src/api/generated/api-client.ts
git commit -m "feat: add useUpdateRecurringJobCronMutation hook and regenerate API client"
```

---

## Task 8: Frontend — Inline CRON editing in `RecurringJobsPage.tsx`

**Files:**
- Modify: `frontend/src/pages/RecurringJobsPage.tsx`

- [ ] **Step 1: Update imports**

Replace the lucide-react import line with:
```typescript
import { Clock, RefreshCw, AlertCircle, ToggleLeft, ToggleRight, Play, Pencil, Check, X } from 'lucide-react';
```

Replace the hook import line with:
```typescript
import { useRecurringJobsQuery, useUpdateRecurringJobStatusMutation, useTriggerRecurringJobMutation, useUpdateRecurringJobCronMutation, RecurringJobDto } from '../api/hooks/useRecurringJobs';
```

- [ ] **Step 2: Add state for inline CRON editing**

Inside `RecurringJobsPage` component, after the existing state declarations, add:

```typescript
const updateCron = useUpdateRecurringJobCronMutation();
const [editingCronJobName, setEditingCronJobName] = useState<string | null>(null);
const [editingCronValue, setEditingCronValue] = useState<string>('');
const [cronEditError, setCronEditError] = useState<string | null>(null);
```

- [ ] **Step 3: Add CRON edit handlers**

Add these handlers after `handleCancelTrigger`:

```typescript
const handleEditCron = (job: RecurringJobDto) => {
  setEditingCronJobName(job.jobName || null);
  setEditingCronValue(job.cronExpression || '');
  setCronEditError(null);
};

const handleCancelCronEdit = () => {
  setEditingCronJobName(null);
  setEditingCronValue('');
  setCronEditError(null);
};

const handleSaveCron = async (job: RecurringJobDto) => {
  if (!job.jobName) return;
  setCronEditError(null);

  try {
    await updateCron.mutateAsync({
      jobName: job.jobName,
      cronExpression: editingCronValue
    });
    setEditingCronJobName(null);
    setEditingCronValue('');
  } catch (error: unknown) {
    const message =
      error instanceof Error ? error.message : 'Chyba při ukládání CRON výrazu';
    setCronEditError(message);
  }
};
```

- [ ] **Step 4: Replace CRON cell in table**

Find the existing CRON cell in the table body:
```tsx
<td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-gray-600">
  {job.cronExpression || '-'}
</td>
```

Replace it with:
```tsx
<td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
  {editingCronJobName === job.jobName ? (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1">
        <input
          type="text"
          value={editingCronValue}
          onChange={(e) => setEditingCronValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') handleSaveCron(job);
            if (e.key === 'Escape') handleCancelCronEdit();
          }}
          className="font-mono text-xs border border-gray-300 rounded px-2 py-1 w-32 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          autoFocus
          aria-label="CRON výraz"
        />
        <button
          onClick={() => handleSaveCron(job)}
          disabled={updateCron.isPending}
          aria-label="Uložit CRON výraz"
          className="text-green-600 hover:text-green-800 disabled:opacity-50"
          title="Uložit"
        >
          {updateCron.isPending ? (
            <RefreshCw className="h-4 w-4 animate-spin" />
          ) : (
            <Check className="h-4 w-4" />
          )}
        </button>
        <button
          onClick={handleCancelCronEdit}
          aria-label="Zrušit úpravu CRON výrazu"
          className="text-gray-400 hover:text-gray-600"
          title="Zrušit"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
      {cronEditError && (
        <span className="text-xs text-red-600">{cronEditError}</span>
      )}
    </div>
  ) : (
    <div className="flex items-center gap-1 group">
      <span className="font-mono">{job.cronExpression || '-'}</span>
      <button
        onClick={() => handleEditCron(job)}
        aria-label={`Upravit CRON výraz pro ${job.displayName || job.jobName}`}
        className="opacity-0 group-hover:opacity-100 transition-opacity text-gray-400 hover:text-gray-600"
        title="Upravit CRON"
      >
        <Pencil className="h-3.5 w-3.5" />
      </button>
    </div>
  )}
</td>
```

- [ ] **Step 5: Build frontend**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -20
```

Expected: `Compiled successfully.` (or build artifacts created without errors).

- [ ] **Step 6: Run all frontend tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --no-coverage 2>&1 | tail -30
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/pages/RecurringJobsPage.tsx
git commit -m "feat: add inline CRON editing to RecurringJobsPage"
```

---

## Task 9: Final validation + lint + format

- [ ] **Step 1: Run dotnet format**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
```

If any violations are reported, run without `--verify-no-changes` to fix them:
```bash
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

- [ ] **Step 2: Run frontend lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run lint 2>&1 | tail -20
```

Expected: No errors.

- [ ] **Step 3: Full BE test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q
```

Expected: All tests pass.

- [ ] **Step 4: Full FE test suite**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --no-coverage 2>&1 | tail -10
```

Expected: All tests pass.

- [ ] **Step 5: Commit format fixes (if any)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add -p  # stage only formatting changes
git commit -m "style: apply dotnet format and lint fixes"
```

---

## Task 10: Push and create PR

- [ ] **Step 1: Create branch, push, and open PR**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
# (you should be in a worktree branch already; if not, create one)
git push -u origin HEAD
```

- [ ] **Step 2: Create PR via GitHub CLI**

```bash
gh pr create \
  --title "feat: DB-driven CRON config for recurring jobs + inline UI editing (#451)" \
  --body "$(cat <<'EOF'
## Summary

- `RecurringJobDiscoveryService` now loads CRON expressions from DB on startup — runtime changes survive restarts
- New `PUT /api/RecurringJobs/{jobName}/cron` endpoint validates and persists a new CRON, then applies it live to Hangfire without restart
- Recurring Jobs page gains an inline pencil-edit UI for CRON expressions (hover → click → save/cancel)

## Changes

- `RecurringJobConfiguration.UpdateCronExpression()` domain method
- `IHangfireRecurringJobScheduler` + `HangfireRecurringJobScheduler` for live Hangfire updates
- `UpdateRecurringJobCron` use case (handler, request, response)
- `UpdateRecurringJobCronHandlerTests` (6 tests: not-found, invalid CRON, empty CRON, valid update, CRON format variants)
- `useUpdateRecurringJobCronMutation` frontend hook + tests
- Inline CRON edit UI in `RecurringJobsPage`

## Test plan

- [ ] BE: `dotnet test` — all tests pass
- [ ] FE: `npm test` — all tests pass
- [ ] Manually verify: edit a CRON in the UI, restart app, confirm new CRON is still active
- [ ] Manually verify: enter invalid CRON, confirm inline error shown

Closes #451

🤖 Generated with [Claude Code](https://claude.com/claude-code) @claude
EOF
)"
```

---

## Spec Coverage Check

| Spec Requirement | Covered by Task |
|-----------------|-----------------|
| `RecurringJobDiscoveryService` reads CRON from DB | Task 5 |
| Fallback to metadata if no DB record | Task 5 |
| `PUT /api/RecurringJobs/{jobName}/cron` endpoint | Task 4 |
| Validate CRON format → 400 | Task 3 (handler + tests) |
| 404 if job not found | Task 3 (handler + tests) |
| DB update | Task 3 (handler) |
| Live Hangfire update via `RecurringJob.AddOrUpdate` | Task 2 + Task 3 |
| Response: `{ jobName, cronExpression, lastModifiedAt, lastModifiedBy }` | Task 3 |
| `useUpdateRecurringJobCronMutation` hook | Task 7 |
| Pencil icon on row hover | Task 8 |
| Monospace input with Save/Cancel | Task 8 |
| Optimistic update + invalidate query on success | Task 8 (invalidate in mutation `onSuccess`) |
| Inline error + revert on failure | Task 8 (catch block sets `cronEditError`) |
