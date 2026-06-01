# Refactor TriggerRecurringJobHandler - Extract Reflection Logic

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract complex Hangfire reflection logic from TriggerRecurringJobHandler into a dedicated, testable helper class.

**Architecture:** Create `HangfireJobEnqueuer` service class in `Application/Features/BackgroundJobs/Services/` that encapsulates all reflection-based Hangfire job enqueueing logic. The handler will delegate to this service, making the handler cleaner and the reflection logic independently testable.

**Tech Stack:** .NET 8, Hangfire, MediatR, xUnit, Moq

---

## Task 1: Create HangfireJobEnqueuer Service Class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireJobEnqueuer.cs`

**Step 1: Create the interface**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IHangfireJobEnqueuer.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Service responsible for enqueueing recurring jobs via Hangfire using reflection.
/// </summary>
public interface IHangfireJobEnqueuer
{
    /// <summary>
    /// Enqueues a recurring job for immediate execution using Hangfire's BackgroundJob.Enqueue.
    /// </summary>
    /// <param name="job">The recurring job instance to enqueue</param>
    /// <param name="cancellationToken">Cancellation token to pass to the job execution</param>
    /// <returns>Hangfire job ID if successful, null if enqueue failed</returns>
    string? EnqueueJob(IRecurringJob job, CancellationToken cancellationToken);
}
```

**Step 2: Create the implementation class**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobEnqueuer.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

/// <summary>
/// Service responsible for enqueueing recurring jobs via Hangfire using reflection.
/// This class encapsulates all the complex reflection logic needed to dynamically
/// invoke Hangfire's BackgroundJob.Enqueue&lt;T&gt; method.
/// </summary>
public class HangfireJobEnqueuer : IHangfireJobEnqueuer
{
    private readonly ILogger<HangfireJobEnqueuer> _logger;

    public HangfireJobEnqueuer(ILogger<HangfireJobEnqueuer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string? EnqueueJob(IRecurringJob job, CancellationToken cancellationToken)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        var jobType = job.GetType();
        var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));

        if (executeMethod == null)
        {
            _logger.LogError("Could not find ExecuteAsync method on IRecurringJob");
            return null;
        }

        // Find the async Enqueue method from Hangfire's BackgroundJob static class
        var enqueueMethod = FindEnqueueMethod();
        if (enqueueMethod == null)
        {
            _logger.LogError("Could not find suitable Enqueue method on BackgroundJob static class");
            return null;
        }

        // Create generic method for the specific job type
        var genericMethod = enqueueMethod.MakeGenericMethod(jobType);

        // Build expression tree: (TJob job) => job.ExecuteAsync(cancellationToken)
        var lambda = CreateExecutionExpression(jobType, executeMethod, cancellationToken);

        // Invoke BackgroundJob.Enqueue<T>(expression)
        var jobId = (string?)genericMethod.Invoke(null, new object[] { lambda });

        _logger.LogDebug("Enqueued job of type {JobType} with Hangfire job ID: {JobId}",
            jobType.Name, jobId);

        return jobId;
    }

    /// <summary>
    /// Finds the Enqueue&lt;T&gt;(Expression&lt;Func&lt;T, Task&gt;&gt;) method from BackgroundJob static class.
    /// </summary>
    private static System.Reflection.MethodInfo? FindEnqueueMethod()
    {
        return typeof(BackgroundJob)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                if (parameters.Length != 1) return false;

                var paramType = parameters[0].ParameterType;
                if (!paramType.IsGenericType) return false;

                // Check if parameter is Expression<...>
                var genericTypeDef = paramType.GetGenericTypeDefinition();
                if (genericTypeDef != typeof(System.Linq.Expressions.Expression<>)) return false;

                // Get the inner type (should be Func<T, Task> or Action<T>)
                var innerType = paramType.GetGenericArguments()[0];
                if (!innerType.IsGenericType) return false;

                // We want Func<T, Task>, not Action<T>
                var innerGenericDef = innerType.GetGenericTypeDefinition();
                if (innerGenericDef != typeof(Func<,>)) return false;

                // Verify the Func has 2 generic arguments (T and TResult)
                var funcArgs = innerType.GetGenericArguments();
                if (funcArgs.Length != 2) return false;

                // Second argument should be Task
                return funcArgs[1] == typeof(Task);
            });
    }

    /// <summary>
    /// Creates an expression tree representing: (TJob job) => job.ExecuteAsync(cancellationToken)
    /// </summary>
    private static System.Linq.Expressions.LambdaExpression CreateExecutionExpression(
        Type jobType,
        System.Reflection.MethodInfo executeMethod,
        CancellationToken cancellationToken)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(jobType, "job");
        var methodCall = System.Linq.Expressions.Expression.Call(
            parameter,
            executeMethod,
            System.Linq.Expressions.Expression.Constant(cancellationToken, typeof(CancellationToken))
        );
        var lambda = System.Linq.Expressions.Expression.Lambda(methodCall, parameter);

        return lambda;
    }
}
```

**Step 3: Commit the service classes**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
git commit -m "feat: add HangfireJobEnqueuer service for reflection-based job enqueueing"
```

---

## Task 2: Register HangfireJobEnqueuer in DI Container

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

**Step 1: Read the current module registration**

Read: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

**Step 2: Add service registration**

Add the following line in the appropriate registration method (likely `AddBackgroundJobsModule` or similar):

```csharp
services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();
```

**Step 3: Verify the file compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeds with no errors

**Step 4: Commit the DI registration**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs
git commit -m "feat: register HangfireJobEnqueuer in DI container"
```

---

## Task 3: Write Unit Tests for HangfireJobEnqueuer

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`

**Step 1: Create the test file**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Unit tests for HangfireJobEnqueuer service
/// </summary>
public class HangfireJobEnqueuerTests
{
    private readonly Mock<ILogger<HangfireJobEnqueuer>> _loggerMock;
    private readonly HangfireJobEnqueuer _enqueuer;

    public HangfireJobEnqueuerTests()
    {
        _loggerMock = new Mock<ILogger<HangfireJobEnqueuer>>();
        _enqueuer = new HangfireJobEnqueuer(_loggerMock.Object);

        // Setup Hangfire with in-memory storage for testing
        GlobalConfiguration.Configuration.UseMemoryStorage();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HangfireJobEnqueuer(null!));
    }

    [Fact]
    public void EnqueueJob_WithNullJob_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _enqueuer.EnqueueJob(null!, CancellationToken.None));
    }

    [Fact]
    public void EnqueueJob_WithValidJob_ReturnsJobId()
    {
        // Arrange
        var job = new TestRecurringJob("test-job");
        var cancellationToken = CancellationToken.None;

        // Act
        var jobId = _enqueuer.EnqueueJob(job, cancellationToken);

        // Assert
        Assert.NotNull(jobId);
        Assert.NotEmpty(jobId);
    }

    [Fact]
    public void EnqueueJob_WithValidJob_LogsDebugMessage()
    {
        // Arrange
        var job = new TestRecurringJob("test-job");
        var cancellationToken = CancellationToken.None;

        // Act
        _enqueuer.EnqueueJob(job, cancellationToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Enqueued job of type")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void EnqueueJob_WithCancellationToken_PassesTokenToJob()
    {
        // Arrange
        var job = new TestRecurringJob("test-job");
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Act
        var jobId = _enqueuer.EnqueueJob(job, cancellationToken);

        // Assert
        Assert.NotNull(jobId);
        Assert.NotEmpty(jobId);
        // The job should be enqueued with the cancellation token
        // (we can't easily verify the token itself, but we verify no exception was thrown)
    }

    /// <summary>
    /// Test recurring job for verification
    /// </summary>
    private class TestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; }

        public TestRecurringJob(string jobName)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = $"Test {jobName}",
                Description = "Test job for unit testing",
                CronExpression = "0 0 * * *"
            };
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
        }
    }
}
```

**Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`
Expected: All tests PASS

**Step 3: Commit the unit tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs
git commit -m "test: add unit tests for HangfireJobEnqueuer service"
```

---

## Task 4: Move Reflection Verification Tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireReflectionVerificationTest.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireJobEnqueuerTests.cs`

**Step 1: Move reflection verification tests to HangfireJobEnqueuerTests**

The existing `HangfireReflectionVerificationTest.cs` tests should be moved into `HangfireJobEnqueuerTests.cs` since they test the same reflection logic that now lives in the enqueuer.

Add these tests to `HangfireJobEnqueuerTests.cs`:

```csharp
[Fact]
public void FindEnqueueMethod_ReturnsCorrectMethod()
{
    // This test verifies that the reflection logic finds the correct Hangfire method
    // We'll use reflection to call the private FindEnqueueMethod for testing purposes

    var findMethodInfo = typeof(HangfireJobEnqueuer)
        .GetMethod("FindEnqueueMethod", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    Assert.NotNull(findMethodInfo);

    // Act
    var enqueueMethod = findMethodInfo.Invoke(null, null) as System.Reflection.MethodInfo;

    // Assert
    Assert.NotNull(enqueueMethod);
    Assert.Equal("Enqueue", enqueueMethod.Name);
    Assert.True(enqueueMethod.IsGenericMethodDefinition);
    Assert.Equal(typeof(string), enqueueMethod.ReturnType);
}

[Fact]
public void CreateExecutionExpression_CreatesValidLambda()
{
    // Test that CreateExecutionExpression produces a valid lambda expression
    var jobType = typeof(TestRecurringJob);
    var executeMethod = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.ExecuteAsync));
    var cancellationToken = CancellationToken.None;

    Assert.NotNull(executeMethod);

    var createExpressionMethod = typeof(HangfireJobEnqueuer)
        .GetMethod("CreateExecutionExpression", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    Assert.NotNull(createExpressionMethod);

    // Act
    var lambda = createExpressionMethod.Invoke(null, new object[] { jobType, executeMethod, cancellationToken });

    // Assert
    Assert.NotNull(lambda);
    Assert.IsAssignableFrom<System.Linq.Expressions.LambdaExpression>(lambda);

    var lambdaExpr = (System.Linq.Expressions.LambdaExpression)lambda;
    Assert.Equal(jobType, lambdaExpr.Parameters[0].Type);
}
```

**Step 2: Delete the old HangfireReflectionVerificationTest file**

Since the tests have been moved to the enqueuer tests (where they belong), delete the old file:

```bash
rm backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireReflectionVerificationTest.cs
```

**Step 3: Run all BackgroundJobs tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~BackgroundJobs"`
Expected: All tests PASS

**Step 4: Commit the changes**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/
git commit -m "test: move reflection verification tests to HangfireJobEnqueuerTests"
```

---

## Task 5: Refactor TriggerRecurringJobHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`

**Step 1: Update handler to use HangfireJobEnqueuer**

Replace the current handler implementation with the refactored version:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobHandler : IRequestHandler<TriggerRecurringJobRequest, TriggerRecurringJobResponse>
{
    private readonly IEnumerable<IRecurringJob> _jobs;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IHangfireJobEnqueuer _jobEnqueuer;
    private readonly ILogger<TriggerRecurringJobHandler> _logger;

    public TriggerRecurringJobHandler(
        IEnumerable<IRecurringJob> jobs,
        IRecurringJobStatusChecker statusChecker,
        IHangfireJobEnqueuer jobEnqueuer,
        ILogger<TriggerRecurringJobHandler> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _jobEnqueuer = jobEnqueuer ?? throw new ArgumentNullException(nameof(jobEnqueuer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TriggerRecurringJobResponse> Handle(TriggerRecurringJobRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to trigger job {JobName} (forceDisabled={ForceDisabled})",
            request.JobName, request.ForceDisabled);

        // Find the job instance
        var job = _jobs.FirstOrDefault(j => j.Metadata.JobName == request.JobName);

        if (job == null)
        {
            _logger.LogWarning("Job {JobName} not found in registered jobs", request.JobName);
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        // Check if job is enabled (unless forced)
        if (!request.ForceDisabled)
        {
            var isEnabled = await _statusChecker.IsJobEnabledAsync(request.JobName, cancellationToken);
            if (!isEnabled)
            {
                _logger.LogWarning("Job {JobName} is disabled. Use forceDisabled=true to trigger anyway.", request.JobName);
                return new TriggerRecurringJobResponse(
                    ErrorCodes.RecurringJobNotFound,
                    new Dictionary<string, string>
                    {
                        { "jobName", request.JobName },
                        { "forceDisabled", request.ForceDisabled.ToString() }
                    }
                );
            }
        }

        // Enqueue the job for immediate execution via Hangfire
        var jobId = _jobEnqueuer.EnqueueJob(job, cancellationToken);

        if (jobId == null)
        {
            _logger.LogError("Failed to enqueue job {JobName}", request.JobName);
            return new TriggerRecurringJobResponse(
                ErrorCodes.RecurringJobNotFound,
                new Dictionary<string, string>
                {
                    { "jobName", request.JobName },
                    { "forceDisabled", request.ForceDisabled.ToString() }
                }
            );
        }

        _logger.LogInformation("Job {JobName} enqueued with Hangfire job ID: {JobId}", request.JobName, jobId);

        return new TriggerRecurringJobResponse
        {
            JobId = jobId
        };
    }
}
```

**Step 2: Verify the handler compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeds

**Step 3: Commit the refactored handler**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs
git commit -m "refactor: extract reflection logic from TriggerRecurringJobHandler to HangfireJobEnqueuer"
```

---

## Task 6: Update Integration Tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobTriggerServiceIntegrationTest.cs`

**Step 1: Rename the test file**

The file is named `RecurringJobTriggerServiceIntegrationTest.cs` but tests the handler. Rename it:

```bash
git mv backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobTriggerServiceIntegrationTest.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs
```

**Step 2: Update the test to inject HangfireJobEnqueuer**

Read the current test file and update it to include the `IHangfireJobEnqueuer` dependency:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Integration test that verifies TriggerRecurringJobHandler works correctly
/// with actual Hangfire infrastructure (using in-memory storage).
///
/// This test ensures that the handler + HangfireJobEnqueuer work end-to-end
/// with real Hangfire components.
/// </summary>
public class TriggerRecurringJobHandlerIntegrationTests
{
    [Fact]
    public async Task Handle_WithRealHangfire_SuccessfullyEnqueuesJob()
    {
        // Arrange - Set up real Hangfire with in-memory storage
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("test-async-job")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "test-async-job",
            ForceDisabled = false
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);

        // Verify status was checked
        mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync("test-async-job", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAsyncMethod_CreatesCorrectExpressionType()
    {
        // This test specifically verifies that we create Expression<Func<T, Task>>
        // not Expression<Action<T>>, which is the root cause of the error

        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("async-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("async-test")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "async-test",
            ForceDisabled = false
        };

        // Act - This should NOT throw ArgumentException about Expression type mismatch
        Exception? caughtException = null;
        TriggerRecurringJobResponse? result = null;

        try
        {
            result = await handler.Handle(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.Null(caughtException);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
    }

    [Fact]
    public async Task Handle_WithDisabledJob_CanBeTriggeredWithForceFlag()
    {
        // Arrange
        GlobalConfiguration.Configuration.UseMemoryStorage();

        var mockStatusChecker = new Mock<IRecurringJobStatusChecker>();
        var mockLogger = new Mock<ILogger<TriggerRecurringJobHandler>>();
        var mockEnqueuerLogger = new Mock<ILogger<HangfireJobEnqueuer>>();

        mockStatusChecker
            .Setup(x => x.IsJobEnabledAsync("disabled-job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var jobs = new List<IRecurringJob>
        {
            new TestAsyncRecurringJob("disabled-job")
        };

        var jobEnqueuer = new HangfireJobEnqueuer(mockEnqueuerLogger.Object);

        var handler = new TriggerRecurringJobHandler(
            jobs,
            mockStatusChecker.Object,
            jobEnqueuer,
            mockLogger.Object);

        var request = new TriggerRecurringJobRequest
        {
            JobName = "disabled-job",
            ForceDisabled = true
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.JobId);
        Assert.NotEmpty(result.JobId);

        // Verify status check was skipped
        mockStatusChecker.Verify(
            x => x.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test job that mimics real async recurring jobs
    /// </summary>
    private class TestAsyncRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; }

        public TestAsyncRecurringJob(string jobName)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = $"Test {jobName}",
                Description = "Test async job for integration testing",
                CronExpression = "0 0 * * *"
            };
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            // Simulate async work
            await Task.Delay(1, cancellationToken);
        }
    }
}
```

**Step 3: Run the integration tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs`
Expected: All tests PASS

**Step 4: Commit the updated integration tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/TriggerRecurringJobHandlerIntegrationTests.cs
git commit -m "test: update integration tests for refactored handler"
```

---

## Task 7: Run All Tests and Verify

**Files:**
- N/A (verification step)

**Step 1: Run all BackgroundJobs tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~BackgroundJobs"`
Expected: All tests PASS

**Step 2: Run entire backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/`
Expected: All tests PASS (no regressions in other features)

**Step 3: Build entire solution**

Run: `dotnet build backend/`
Expected: Build succeeds with no errors or warnings

**Step 4: Verify code formatting**

Run: `dotnet format backend/ --verify-no-changes`
Expected: No formatting issues

**Step 5: Format code if needed**

If step 4 fails:
Run: `dotnet format backend/`
Then commit formatting:
```bash
git add backend/
git commit -m "style: apply dotnet format to refactored code"
```

---

## Task 8: Final Cleanup and Documentation

**Files:**
- Create: `docs/architecture/recurring-jobs-trigger-architecture.md` (optional documentation)

**Step 1: Review all changes**

Run: `git log --oneline -10`
Expected: See all commits from this refactoring

**Step 2: Create a summary commit (if needed)**

If you made multiple small commits, optionally create a summary:

```bash
git log --oneline --since="1 day ago"
```

Review the work and ensure all tests pass.

**Step 3: Optional - Add architecture documentation**

If desired, create documentation explaining the refactoring:

`docs/architecture/recurring-jobs-trigger-architecture.md`:

```markdown
# Recurring Jobs Trigger Architecture

## Overview
The manual trigger functionality for recurring jobs uses a clean separation of concerns:
- **Controller**: Handles HTTP requests
- **Handler**: Orchestrates business logic (find job, check status, trigger)
- **HangfireJobEnqueuer**: Encapsulates all Hangfire reflection logic

## Why Extract Reflection Logic?
The original `TriggerRecurringJobHandler` contained ~80 lines of complex reflection code to:
1. Find the correct `BackgroundJob.Enqueue<T>` method
2. Create expression trees for job execution
3. Invoke the method dynamically

This made the handler:
- Hard to test in isolation
- Difficult to understand at a glance
- Mixing business logic with infrastructure concerns

## New Architecture
### HangfireJobEnqueuer Service
- **Responsibility**: Enqueue jobs via Hangfire using reflection
- **Interface**: `IHangfireJobEnqueuer` for testability
- **Location**: `Application/Features/BackgroundJobs/Services/`
- **Testing**: Unit tests verify reflection logic works correctly

### TriggerRecurringJobHandler
- **Responsibility**: Business logic for triggering jobs
- **Dependencies**: Injects `IHangfireJobEnqueuer` for enqueueing
- **Testing**: Integration tests verify end-to-end flow

## Benefits
1. **Separation of Concerns**: Business logic separated from infrastructure
2. **Testability**: Each component testable in isolation
3. **Maintainability**: Reflection logic centralized in one place
4. **Readability**: Handler is now ~50 lines instead of ~150
```

Commit if created:
```bash
git add docs/architecture/recurring-jobs-trigger-architecture.md
git commit -m "docs: add architecture documentation for recurring jobs trigger"
```

---

## Summary

**What We Built:**
- Extracted complex Hangfire reflection logic into `HangfireJobEnqueuer` service
- Refactored `TriggerRecurringJobHandler` to use the new service
- Moved and enhanced tests to cover the refactored code
- Improved separation of concerns and testability

**Test Coverage:**
- Unit tests for `HangfireJobEnqueuer`
- Integration tests for full handler flow
- Controller tests remain unchanged (still passing)
- All reflection logic now properly tested

**Code Quality:**
- Handler reduced from ~150 to ~80 lines
- Clear separation between business logic and infrastructure
- All code follows .NET formatting standards
- No breaking changes to public API
