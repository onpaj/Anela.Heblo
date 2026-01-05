# Manual Trigger for Recurring Jobs Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add ability to manually trigger recurring jobs with confirmation dialog for disabled jobs (fire-and-forget approach).

**Architecture:** Create `IRecurringJobTriggerService` that resolves job instances from DI and enqueues them via Hangfire `BackgroundJob.Enqueue()`. Add new API endpoint `POST /api/recurringjobs/{jobName}/trigger` with MediatR handler. Frontend adds "Run Now" button with confirmation dialog for disabled jobs.

**Tech Stack:** .NET 8, Hangfire, MediatR, EF Core, React, TypeScript, TanStack Query

---

## Task 1: Domain Layer - Create IRecurringJobTriggerService Interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs`

**Step 1: Create interface**

Create: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs`

```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

/// <summary>
/// Service for manually triggering recurring jobs on-demand
/// </summary>
public interface IRecurringJobTriggerService
{
    /// <summary>
    /// Trigger a recurring job immediately (fire-and-forget)
    /// </summary>
    /// <param name="jobName">The job name to trigger</param>
    /// <param name="forceDisabled">If true, triggers even if job is disabled</param>
    /// <returns>Job ID from Hangfire, or null if job not found</returns>
    Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false);
}
```

**Step 2: Build domain layer**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`

Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs
git commit -m "feat: add IRecurringJobTriggerService interface for manual job triggering"
```

---

## Task 2: Application Layer - Implement RecurringJobTriggerService

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobTriggerServiceTests.cs`

**Step 1: Write failing test**

Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobTriggerServiceTests.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobTriggerServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker;
    private readonly Mock<ILogger<RecurringJobTriggerService>> _mockLogger;
    private readonly RecurringJobTriggerService _service;

    public RecurringJobTriggerServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        _mockLogger = new Mock<ILogger<RecurringJobTriggerService>>();
        _service = new RecurringJobTriggerService(
            _mockServiceProvider.Object,
            _mockStatusChecker.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task TriggerJobAsync_ShouldReturnNull_WhenJobNotFound()
    {
        // Arrange
        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);

        var mockScopeServiceProvider = new Mock<IServiceProvider>();
        mockScopeServiceProvider
            .Setup(x => x.GetService(typeof(IEnumerable<IRecurringJob>)))
            .Returns(new List<IRecurringJob>());

        mockScope.Setup(x => x.ServiceProvider).Returns(mockScopeServiceProvider.Object);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        // Act
        var result = await _service.TriggerJobAsync("nonexistent-job", false);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TriggerJobAsync_ShouldReturnNull_WhenJobDisabledAndNotForced()
    {
        // Arrange
        var mockJob = new Mock<IRecurringJob>();
        mockJob.Setup(x => x.Metadata).Returns(new RecurringJobMetadata
        {
            JobName = "test-job",
            DisplayName = "Test Job",
            Description = "Test",
            CronExpression = "0 * * * *"
        });

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);

        var mockScopeServiceProvider = new Mock<IServiceProvider>();
        mockScopeServiceProvider
            .Setup(x => x.GetService(typeof(IEnumerable<IRecurringJob>)))
            .Returns(new List<IRecurringJob> { mockJob.Object });

        mockScope.Setup(x => x.ServiceProvider).Returns(mockScopeServiceProvider.Object);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        _mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("test-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.TriggerJobAsync("test-job", false);

        // Assert
        Assert.Null(result);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobTriggerServiceTests"`

Expected: FAIL with "RecurringJobTriggerService does not exist"

**Step 3: Create RecurringJobTriggerService implementation**

Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs`

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public class RecurringJobTriggerService : IRecurringJobTriggerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<RecurringJobTriggerService> _logger;

    public RecurringJobTriggerService(
        IServiceProvider serviceProvider,
        IRecurringJobStatusChecker statusChecker,
        ILogger<RecurringJobTriggerService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false)
    {
        _logger.LogInformation("Attempting to trigger job {JobName} (forceDisabled={ForceDisabled})",
            jobName, forceDisabled);

        // Find the job instance from DI
        using var scope = _serviceProvider.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>();
        var job = jobs.FirstOrDefault(j => j.Metadata.JobName == jobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in registered jobs", jobName);
            return null;
        }

        // Check if job is enabled (unless forced)
        if (!forceDisabled)
        {
            var isEnabled = await _statusChecker.IsJobEnabledAsync(jobName);
            if (!isEnabled)
            {
                _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", jobName);
                return null;
            }
        }

        // Enqueue the job for immediate execution via Hangfire
        var jobType = job.GetType();
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));

        if (executeMethod == null)
        {
            _logger.LogError("Could not find ExecuteAsync method on IRecurringJob");
            return null;
        }

        // Use Hangfire's Enqueue method with reflection
        var enqueueMethod = typeof(IBackgroundJobClient)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length == 1 &&
                       parameters[0].ParameterType.IsGenericType &&
                       parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(System.Linq.Expressions.Expression<>);
            });

        if (enqueueMethod == null)
        {
            _logger.LogError("Could not find suitable Enqueue method on IBackgroundJobClient");
            return null;
        }

        var genericMethod = enqueueMethod.MakeGenericMethod(jobType);

        // Create lambda: job => job.ExecuteAsync(default)
        var parameter = System.Linq.Expressions.Expression.Parameter(jobType, "job");
        var methodCall = System.Linq.Expressions.Expression.Call(
            parameter,
            executeMethod,
            System.Linq.Expressions.Expression.Default(typeof(CancellationToken))
        );
        var lambda = System.Linq.Expressions.Expression.Lambda(methodCall, parameter);

        // Enqueue the job
        var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        var jobId = (string?)genericMethod.Invoke(backgroundJobClient, new object[] { lambda });

        _logger.LogInformation("Job {JobName} enqueued with Hangfire job ID: {JobId}", jobName, jobId);

        return jobId;
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobTriggerServiceTests"`

Expected: PASS

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobTriggerServiceTests.cs
git commit -m "feat: implement RecurringJobTriggerService for manual job execution"
```

---

## Task 3: Application Layer - Register TriggerService in DI

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

**Step 1: Add service registration**

Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

Add after line 14:

```csharp
        // Register recurring job trigger service
        services.AddScoped<IRecurringJobTriggerService, RecurringJobTriggerService>();
```

**Step 2: Build application layer**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

Expected: Build succeeds

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs
git commit -m "feat: register RecurringJobTriggerService in DI container"
```

---

## Task 4: Application Layer - Create TriggerRecurringJob Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs`

**Step 1: Write failing test**

Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs`

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class TriggerRecurringJobHandlerTests
{
    private readonly Mock<IRecurringJobTriggerService> _mockTriggerService;
    private readonly TriggerRecurringJobHandler _handler;

    public TriggerRecurringJobHandlerTests()
    {
        _mockTriggerService = new Mock<IRecurringJobTriggerService>();
        _handler = new TriggerRecurringJobHandler(_mockTriggerService.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccess_WhenJobTriggeredSuccessfully()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("test-job", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("job-id-123");

        var request = new TriggerRecurringJobRequest
        {
            JobName = "test-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-id-123", result.JobId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenJobNotFound()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("nonexistent-job", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "nonexistent-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.JobId);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenJobDisabledAndNotForced()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("disabled-job", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = false
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.JobId);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccess_WhenJobDisabledButForced()
    {
        // Arrange
        _mockTriggerService
            .Setup(x => x.TriggerJobAsync("disabled-job", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("job-id-456");

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("job-id-456", result.JobId);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TriggerRecurringJobHandlerTests"`

Expected: FAIL with "TriggerRecurringJobHandler does not exist"

**Step 3: Create Request class**

Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobRequest : IRequest<TriggerRecurringJobResponse>
{
    public string JobName { get; set; } = string.Empty;
    public bool ForceDisabled { get; set; }
}
```

**Step 4: Create Response class**

Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobResponse.cs`

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobResponse
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Step 5: Create Handler class**

Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobHandler : IRequestHandler<TriggerRecurringJobRequest, TriggerRecurringJobResponse>
{
    private readonly IRecurringJobTriggerService _triggerService;

    public TriggerRecurringJobHandler(IRecurringJobTriggerService triggerService)
    {
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
    }

    public async Task<TriggerRecurringJobResponse> Handle(TriggerRecurringJobRequest request, CancellationToken cancellationToken)
    {
        var jobId = await _triggerService.TriggerJobAsync(request.JobName, request.ForceDisabled, cancellationToken);

        if (jobId == null)
        {
            return new TriggerRecurringJobResponse
            {
                Success = false,
                ErrorMessage = $"Job '{request.JobName}' not found or is disabled (use forceDisabled to override)"
            };
        }

        return new TriggerRecurringJobResponse
        {
            Success = true,
            JobId = jobId
        };
    }
}
```

**Step 6: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TriggerRecurringJobHandlerTests"`

Expected: PASS

**Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/ backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerTests.cs
git commit -m "feat: add TriggerRecurringJob use case with handler"
```

---

## Task 5: Fix IRecurringJobTriggerService Signature

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs`

**Step 1: Update interface to accept CancellationToken**

Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs`

Change line 16:

```csharp
    Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false, CancellationToken cancellationToken = default);
```

**Step 2: Update implementation signature**

Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs`

Change line 20:

```csharp
    public async Task<string?> TriggerJobAsync(string jobName, bool forceDisabled = false, CancellationToken cancellationToken = default)
```

Update line 39:

```csharp
            var isEnabled = await _statusChecker.IsJobEnabledAsync(jobName, cancellationToken);
```

**Step 3: Run tests to verify they still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobTriggerServiceTests"`

Expected: PASS

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TriggerRecurringJobHandlerTests"`

Expected: PASS

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobTriggerService.cs backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobTriggerService.cs
git commit -m "fix: add CancellationToken parameter to TriggerJobAsync"
```

---

## Task 6: API Layer - Add Trigger Endpoint to Controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs`

**Step 1: Write integration test**

Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs`

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobsControllerTriggerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ApplicationDbContext _context;

    public RecurringJobsControllerTriggerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;

        var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Seed test data
        SeedTestData();

        _client = _factory.CreateClient();
    }

    private void SeedTestData()
    {
        _context.RecurringJobConfigurations.RemoveRange(_context.RecurringJobConfigurations);

        var enabledJob = new RecurringJobConfiguration
        {
            JobName = "purchase-price-recalculation",
            DisplayName = "Purchase Price Recalculation",
            Description = "Test",
            CronExpression = "0 * * * *",
            IsEnabled = true,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = "system"
        };

        var disabledJob = new RecurringJobConfiguration
        {
            JobName = "product-weight-recalculation",
            DisplayName = "Product Weight Recalculation",
            Description = "Test",
            CronExpression = "0 * * * *",
            IsEnabled = false,
            LastModifiedAt = DateTime.UtcNow,
            LastModifiedBy = "system"
        };

        _context.RecurringJobConfigurations.Add(enabledJob);
        _context.RecurringJobConfigurations.Add(disabledJob);
        _context.SaveChanges();
    }

    [Fact]
    public async Task TriggerJob_ShouldReturnSuccess_WhenJobIsEnabled()
    {
        // Arrange
        var request = new { forceDisabled = false };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recurringjobs/purchase-price-recalculation/trigger", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TriggerJobResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
    }

    [Fact]
    public async Task TriggerJob_ShouldReturnBadRequest_WhenJobIsDisabledAndNotForced()
    {
        // Arrange
        var request = new { forceDisabled = false };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recurringjobs/product-weight-recalculation/trigger", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TriggerJob_ShouldReturnSuccess_WhenJobIsDisabledButForced()
    {
        // Arrange
        var request = new { forceDisabled = true };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recurringjobs/product-weight-recalculation/trigger", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TriggerJobResponseDto>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
    }

    [Fact]
    public async Task TriggerJob_ShouldReturnNotFound_WhenJobDoesNotExist()
    {
        // Arrange
        var request = new { forceDisabled = false };

        // Act
        var response = await _client.PostAsJsonAsync("/api/recurringjobs/nonexistent-job/trigger", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private class TriggerJobResponseDto
    {
        public bool Success { get; set; }
        public string? JobId { get; set; }
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobsControllerTriggerTests"`

Expected: FAIL with 404 - endpoint does not exist

**Step 3: Add trigger endpoint to controller**

Modify: `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs`

Add after line 64 (after UpdateJobStatus method):

```csharp
    /// <summary>
    /// Manually trigger a recurring job to run immediately (fire-and-forget)
    /// </summary>
    /// <param name="jobName">The name of the job to trigger</param>
    /// <param name="request">Trigger options including forceDisabled flag</param>
    /// <returns>Job trigger result with Hangfire job ID</returns>
    [HttpPost("{jobName}/trigger")]
    [ProducesResponseType(typeof(TriggerRecurringJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TriggerRecurringJobResponse>> TriggerJob(
        string jobName,
        [FromBody] TriggerJobRequestBody request)
    {
        var mediatrRequest = new TriggerRecurringJobRequest
        {
            JobName = jobName,
            ForceDisabled = request.ForceDisabled
        };

        var response = await _mediator.Send(mediatrRequest);

        return HandleResponse(response);
    }
```

Add after the UpdateJobStatusRequestBody class (around line 76):

```csharp
/// <summary>
/// Request body for triggering a recurring job
/// </summary>
public class TriggerJobRequestBody
{
    /// <summary>
    /// If true, triggers the job even if it's currently disabled
    /// </summary>
    public bool ForceDisabled { get; set; }
}
```

Add using statement at top:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
```

**Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobsControllerTriggerTests"`

Expected: PASS

**Step 5: Build API to regenerate OpenAPI client**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

Expected: Build succeeds and frontend API client regenerated

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobsControllerTriggerTests.cs frontend/src/api/generated/
git commit -m "feat: add POST /api/recurringjobs/{jobName}/trigger endpoint"
```

---

## Task 7: Frontend - Add Trigger Job Mutation Hook

**Files:**
- Modify: `frontend/src/api/hooks/useRecurringJobs.ts`

**Step 1: Add trigger mutation hook**

Modify: `frontend/src/api/hooks/useRecurringJobs.ts`

Add after line 54 (after useUpdateRecurringJobStatusMutation):

```typescript
/**
 * Hook to manually trigger a recurring job
 * Uses generated API client method: recurringJobs_TriggerJob
 */
export const useTriggerRecurringJobMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      jobName,
      forceDisabled
    }: {
      jobName: string;
      forceDisabled: boolean;
    }) => {
      const client = getAuthenticatedApiClient();
      const request = new TriggerJobRequestBody({ forceDisabled });
      return await client.recurringJobs_TriggerJob(jobName, request);
    },
    onSuccess: () => {
      // Optionally refetch jobs list (not strictly necessary for fire-and-forget)
      queryClient.invalidateQueries({ queryKey: recurringJobsKeys.list() });
    },
  });
};
```

Add import at top (around line 5):

```typescript
  TriggerJobRequestBody,
  type TriggerRecurringJobResponse
```

**Step 2: Verify TypeScript compilation**

Run: `cd frontend && npx tsc --noEmit`

Expected: No errors

**Step 3: Commit**

```bash
git add frontend/src/api/hooks/useRecurringJobs.ts
git commit -m "feat: add useTriggerRecurringJobMutation hook for manual job triggering"
```

---

## Task 8: Frontend - Add Confirmation Dialog Component

**Files:**
- Create: `frontend/src/components/dialogs/ConfirmTriggerJobDialog.tsx`

**Step 1: Create confirmation dialog component**

Create: `frontend/src/components/dialogs/ConfirmTriggerJobDialog.tsx`

```typescript
import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface ConfirmTriggerJobDialogProps {
  isOpen: boolean;
  jobName: string;
  jobDisplayName: string;
  isJobDisabled: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

const ConfirmTriggerJobDialog: React.FC<ConfirmTriggerJobDialogProps> = ({
  isOpen,
  jobName,
  jobDisplayName,
  isJobDisabled,
  onConfirm,
  onCancel
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={onCancel}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white rounded-lg shadow-xl max-w-md w-full p-6">
          {/* Close button */}
          <button
            onClick={onCancel}
            className="absolute top-4 right-4 text-gray-400 hover:text-gray-600"
          >
            <X className="h-5 w-5" />
          </button>

          {/* Icon */}
          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-yellow-100 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-yellow-600" />
          </div>

          {/* Title */}
          <h3 className="text-lg font-semibold text-gray-900 text-center mb-2">
            Spustit úlohu nyní?
          </h3>

          {/* Message */}
          <div className="text-sm text-gray-600 text-center mb-6">
            <p className="mb-2">
              Chystáte se manuálně spustit úlohu:
            </p>
            <p className="font-semibold text-gray-900">{jobDisplayName}</p>
            <p className="text-xs text-gray-500 mt-1">({jobName})</p>

            {isJobDisabled && (
              <div className="mt-4 p-3 bg-yellow-50 border border-yellow-200 rounded-md">
                <p className="text-yellow-800 font-medium">
                  ⚠️ Úloha je aktuálně vypnutá
                </p>
                <p className="text-yellow-700 text-xs mt-1">
                  Spuštěním potvrdíte, že chcete tuto úlohu spustit i když je vypnutá.
                </p>
              </div>
            )}
          </div>

          {/* Actions */}
          <div className="flex gap-3">
            <button
              onClick={onCancel}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition-colors"
            >
              Zrušit
            </button>
            <button
              onClick={onConfirm}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 transition-colors"
            >
              Spustit
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmTriggerJobDialog;
```

**Step 2: Verify TypeScript compilation**

Run: `cd frontend && npx tsc --noEmit`

Expected: No errors

**Step 3: Commit**

```bash
git add frontend/src/components/dialogs/ConfirmTriggerJobDialog.tsx
git commit -m "feat: add ConfirmTriggerJobDialog component for job trigger confirmation"
```

---

## Task 9: Frontend - Add "Run Now" Button to RecurringJobsPage

**Files:**
- Modify: `frontend/src/pages/RecurringJobsPage.tsx`

**Step 1: Add trigger functionality to RecurringJobsPage**

Modify: `frontend/src/pages/RecurringJobsPage.tsx`

Add import at top (around line 3):

```typescript
import { Clock, RefreshCw, AlertCircle, ToggleLeft, ToggleRight, Play } from 'lucide-react';
import { useRecurringJobsQuery, useUpdateRecurringJobStatusMutation, useTriggerRecurringJobMutation, RecurringJobDto } from '../api/hooks/useRecurringJobs';
import ConfirmTriggerJobDialog from '../components/dialogs/ConfirmTriggerJobDialog';
```

Add state variables after line 9 (after existing useState):

```typescript
  const triggerJob = useTriggerRecurringJobMutation();
  const [triggeringJobName, setTriggeringJobName] = useState<string | null>(null);
  const [confirmDialogJob, setConfirmDialogJob] = useState<RecurringJobDto | null>(null);
```

Add trigger handler after handleToggle function (around line 25):

```typescript
  const handleTriggerClick = (job: RecurringJobDto) => {
    setConfirmDialogJob(job);
  };

  const handleConfirmTrigger = async () => {
    if (!confirmDialogJob?.jobName) return;

    setTriggeringJobName(confirmDialogJob.jobName);
    setConfirmDialogJob(null);

    try {
      await triggerJob.mutateAsync({
        jobName: confirmDialogJob.jobName,
        forceDisabled: !confirmDialogJob.isEnabled
      });

      // Optional: Show success notification
      console.log(`Job ${confirmDialogJob.jobName} triggered successfully`);
    } catch (error) {
      console.error('Chyba při spouštění jobu:', error);
      // Optional: Show error notification
    } finally {
      setTriggeringJobName(null);
    }
  };

  const handleCancelTrigger = () => {
    setConfirmDialogJob(null);
  };
```

Add new "Actions" column header in table (around line 140):

```typescript
                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Akce
                </th>
```

Add new "Actions" column in table row (after the Status cell, around line 192):

```typescript
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-center">
                    <button
                      onClick={() => handleTriggerClick(job)}
                      disabled={triggeringJobName === job.jobName}
                      className={`
                        inline-flex items-center px-3 py-1.5 rounded-md text-xs font-medium transition-all duration-200
                        ${triggeringJobName === job.jobName
                          ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                          : 'bg-indigo-100 text-indigo-800 hover:bg-indigo-200 cursor-pointer'
                        }
                      `}
                      title="Spustit úlohu nyní"
                    >
                      {triggeringJobName === job.jobName ? (
                        <RefreshCw className="h-3.5 w-3.5 mr-1 animate-spin" />
                      ) : (
                        <Play className="h-3.5 w-3.5 mr-1" />
                      )}
                      Spustit nyní
                    </button>
                  </td>
```

Add dialog component at the end of the component (before closing div, around line 198):

```typescript
      {/* Confirmation Dialog */}
      <ConfirmTriggerJobDialog
        isOpen={confirmDialogJob !== null}
        jobName={confirmDialogJob?.jobName || ''}
        jobDisplayName={confirmDialogJob?.displayName || confirmDialogJob?.jobName || ''}
        isJobDisabled={!confirmDialogJob?.isEnabled}
        onConfirm={handleConfirmTrigger}
        onCancel={handleCancelTrigger}
      />
    </div>
```

**Step 2: Verify TypeScript compilation**

Run: `cd frontend && npx tsc --noEmit`

Expected: No errors

**Step 3: Test locally (optional)**

Run: `cd frontend && npm start`

Navigate to http://localhost:3000/recurring-jobs

Verify:
- "Spustit nyní" button appears in Actions column
- Clicking button opens confirmation dialog
- Clicking "Spustit" triggers the job
- Dialog shows warning for disabled jobs

**Step 4: Commit**

```bash
git add frontend/src/pages/RecurringJobsPage.tsx
git commit -m "feat: add 'Run Now' button with confirmation dialog to RecurringJobsPage"
```

---

## Task 10: Backend - Run All Tests

**Files:**
- None (verification task)

**Step 1: Run complete backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: All tests PASS

**Step 2: If any tests fail, fix them**

Fix any failing tests before proceeding.

**Step 3: Commit fixes if needed**

```bash
git add backend/test/
git commit -m "fix: resolve test failures after manual trigger implementation"
```

---

## Task 11: Frontend - Build Verification

**Files:**
- None (verification task)

**Step 1: Run frontend build**

Run: `cd frontend && npm run build`

Expected: Build succeeds with no errors

**Step 2: If build fails, fix errors**

Fix any TypeScript or build errors.

**Step 3: Commit fixes if needed**

```bash
git add frontend/
git commit -m "fix: resolve frontend build errors"
```

---

## Task 12: Backend - Format Check and Fix

**Files:**
- Multiple (formatting task)

**Step 1: Run format check**

Run: `dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes`

Expected: No formatting violations

**Step 2: If violations found, fix them**

Run: `dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

Expected: Formatting applied

**Step 3: Commit formatting changes**

```bash
git add backend/
git commit -m "style: apply dotnet format to backend code"
```

---

## Task 13: Documentation - Update Feature Documentation

**Files:**
- Modify: `docs/features/recurring-jobs-management.md` (if exists)
- Or create if doesn't exist

**Step 1: Add manual trigger documentation**

Modify or create: `docs/features/recurring-jobs-management.md`

Add section after "Usage" section:

```markdown
### Manually Triggering Jobs

Jobs can be manually triggered on-demand via the "Spustit nyní" (Run Now) button in the UI:

1. Navigate to `/recurring-jobs` page
2. Click "Spustit nyní" button for desired job
3. Confirm the action in the dialog
   - If job is disabled, dialog will show warning
   - Confirmation is required for both enabled and disabled jobs
4. Job is immediately enqueued in Hangfire (fire-and-forget)
5. Job execution can be monitored in Hangfire dashboard

**API Endpoint:**
- `POST /api/recurringjobs/{jobName}/trigger`
- Request body: `{ "forceDisabled": true/false }`
- Response: `{ "success": true, "jobId": "hangfire-job-id" }`

**Behavior:**
- Enabled jobs: Trigger immediately
- Disabled jobs: Require `forceDisabled: true` parameter
- Fire-and-forget: API returns immediately, job runs asynchronously
```

**Step 2: Commit documentation**

```bash
git add docs/features/
git commit -m "docs: add manual trigger documentation to recurring jobs feature"
```

---

## Task 14: E2E Test - Create Playwright Test for Manual Trigger

**Files:**
- Create: `frontend/test/e2e/recurring-jobs/manual-trigger.spec.ts`

**Step 1: Create Playwright test**

Create: `frontend/test/e2e/recurring-jobs/manual-trigger.spec.ts`

```typescript
import { test, expect } from '@playwright/test';

test.describe('Recurring Jobs - Manual Trigger', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('https://heblo.stg.anela.cz');
    await page.waitForLoadState('networkidle');

    // Navigate to recurring jobs page
    await page.click('text=Naplánované úlohy');
    await page.waitForURL('**/recurring-jobs');
  });

  test('should show "Run Now" button for all jobs', async ({ page }) => {
    // Verify at least one "Spustit nyní" button exists
    const runButtons = page.locator('button:has-text("Spustit nyní")');
    await expect(runButtons.first()).toBeVisible();

    // Verify button has Play icon
    await expect(runButtons.first().locator('svg')).toBeVisible();
  });

  test('should open confirmation dialog when clicking "Run Now"', async ({ page }) => {
    // Click first "Spustit nyní" button
    const firstRunButton = page.locator('button:has-text("Spustit nyní")').first();
    await firstRunButton.click();

    // Verify dialog opened
    await expect(page.locator('text=Spustit úlohu nyní?')).toBeVisible();

    // Verify dialog has job name displayed
    await expect(page.locator('text=Chystáte se manuálně spustit úlohu:')).toBeVisible();

    // Verify dialog has action buttons
    await expect(page.locator('button:has-text("Zrušit")')).toBeVisible();
    await expect(page.locator('button:has-text("Spustit")')).toBeVisible();
  });

  test('should close dialog when clicking "Cancel"', async ({ page }) => {
    // Open dialog
    const firstRunButton = page.locator('button:has-text("Spustit nyní")').first();
    await firstRunButton.click();
    await expect(page.locator('text=Spustit úlohu nyní?')).toBeVisible();

    // Click cancel
    await page.locator('button:has-text("Zrušit")').click();

    // Verify dialog closed
    await expect(page.locator('text=Spustit úlohu nyní?')).not.toBeVisible();
  });

  test('should trigger enabled job when confirmed', async ({ page }) => {
    // Find first enabled job's run button
    const firstEnabledRow = page.locator('tr:has(button.bg-emerald-100)').first();
    const runButton = firstEnabledRow.locator('button:has-text("Spustit nyní")');

    await runButton.click();

    // Confirm in dialog
    await page.locator('button:has-text("Spustit")').click();

    // Wait for dialog to close
    await expect(page.locator('text=Spustit úlohu nyní?')).not.toBeVisible();

    // Verify button shows loading state briefly (might be very fast)
    // This is best-effort as fire-and-forget is very quick
    await page.waitForTimeout(500);
  });

  test('should show warning for disabled job in dialog', async ({ page }) => {
    // Find first disabled job's run button
    const firstDisabledRow = page.locator('tr:has(button.bg-gray-100)').first();

    if (await firstDisabledRow.count() === 0) {
      test.skip('No disabled jobs found for testing');
    }

    const runButton = firstDisabledRow.locator('button:has-text("Spustit nyní")');
    await runButton.click();

    // Verify warning message in dialog
    await expect(page.locator('text=Úloha je aktuálně vypnutá')).toBeVisible();
    await expect(page.locator('text=Spuštěním potvrdíte, že chcete tuto úlohu spustit i když je vypnutá.')).toBeVisible();
  });

  test('should trigger disabled job when forced', async ({ page }) => {
    // Find first disabled job's run button
    const firstDisabledRow = page.locator('tr:has(button.bg-gray-100)').first();

    if (await firstDisabledRow.count() === 0) {
      test.skip('No disabled jobs found for testing');
    }

    const runButton = firstDisabledRow.locator('button:has-text("Spustit nyní")');
    await runButton.click();

    // Confirm in dialog (with forceDisabled=true)
    await page.locator('button:has-text("Spustit")').click();

    // Verify dialog closed
    await expect(page.locator('text=Spustit úlohu nyní?')).not.toBeVisible();
  });

  test('should close dialog when clicking backdrop', async ({ page }) => {
    // Open dialog
    const firstRunButton = page.locator('button:has-text("Spustit nyní")').first();
    await firstRunButton.click();
    await expect(page.locator('text=Spustit úlohu nyní?')).toBeVisible();

    // Click backdrop (outside dialog)
    await page.locator('div.bg-black.bg-opacity-50').click({ position: { x: 10, y: 10 } });

    // Verify dialog closed
    await expect(page.locator('text=Spustit úlohu nyní?')).not.toBeVisible();
  });
});
```

**Step 2: Run Playwright test locally (optional)**

Run: `./scripts/run-playwright-tests.sh manual-trigger`

Expected: Tests PASS

**Step 3: Commit test**

```bash
git add frontend/test/e2e/recurring-jobs/manual-trigger.spec.ts
git commit -m "test: add E2E tests for manual job triggering"
```

---

## Task 15: Final Verification and Summary

**Files:**
- None (verification task)

**Step 1: Run complete backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: All tests PASS

**Step 2: Run complete frontend build**

Run: `cd frontend && npm run build`

Expected: Build succeeds

**Step 3: Run complete E2E test suite**

Run: `./scripts/run-playwright-tests.sh`

Expected: All tests PASS

**Step 4: Review git log**

Run: `git log --oneline --graph -15`

Verify all commits are present and descriptive.

**Step 5: Create implementation summary**

The implementation is complete with:

✅ **Backend (8 files created, 2 modified):**
- `IRecurringJobTriggerService` interface
- `RecurringJobTriggerService` implementation
- `TriggerRecurringJob` use case (Request, Response, Handler)
- `POST /api/recurringjobs/{jobName}/trigger` endpoint
- Comprehensive unit and integration tests

✅ **Frontend (3 files created, 2 modified):**
- `useTriggerRecurringJobMutation` hook
- `ConfirmTriggerJobDialog` component
- "Spustit nyní" button in RecurringJobsPage
- Confirmation dialog with warning for disabled jobs

✅ **Testing:**
- Backend unit tests for TriggerService
- Backend integration tests for API endpoint
- Frontend E2E tests for manual triggering

✅ **Documentation:**
- Updated feature documentation

**Architecture:**
- Fire-and-forget approach using Hangfire
- Confirmation required for all jobs
- Warning displayed for disabled jobs
- forceDisabled parameter to trigger disabled jobs

---

## Implementation Complete ✅

All tasks completed successfully. The manual trigger feature is fully implemented with:
- Backend API endpoint for triggering jobs
- Frontend UI with "Run Now" button and confirmation dialog
- Comprehensive test coverage
- Documentation
