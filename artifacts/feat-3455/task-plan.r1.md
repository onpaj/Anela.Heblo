# Task Plan: Open/Closed DQT Runner Dispatch (RunDqtHandler / GetDqtRunDetailHandler)

## Overview

This is a small, tightly-scoped backend refactor inside `backend/src/Anela.Heblo.Application/Features/DataQuality/`. It replaces two binary/implicit dispatch points (`RunDqtHandler`'s invoice-vs-drift `if/else`, and `GetDqtRunDetailHandler`'s implicit invoice-vs-everything-else fallthrough) with explicit, extensible, fail-fast dispatch. All architectural decisions are final (see arch-review.r1.md, not available to task executors — this plan is self-contained instead).

The work is split into 3 serially-ordered tasks, each of which leaves the build green on its own:

1. **dqt-job-runner-interface-and-di** — adds the new `IDqtJobRunner` interface, implements it on both existing runner classes (purely additive — no existing method bodies change), registers it in DI, and adds the new `ErrorCodes.DqtUnsupportedTestType = 2204` entry. Nothing here changes any handler's behavior yet, so the build stays green and all existing tests keep passing untouched.
2. **rundqt-handler-dispatch** — depends on task 1 (`IDqtJobRunner` must exist). Replaces `RunDqtHandler.Handle`'s binary dispatch with `IDqtJobRunner` resolution, and rewrites `RunDqtHandlerTests.cs`'s mock wiring (which currently stubs `IServiceProvider.GetService(typeof(IInvoiceDqtJobRunner))` and will silently stop matching once the handler calls `GetServices<IDqtJobRunner>()`).
3. **getdqtrundetail-handler-dispatch** — independent of task 2's code (same DataQuality module, different handler), but ordered last since it also depends on task 1's new `ErrorCodes.DqtUnsupportedTestType`. Replaces `GetDqtRunDetailHandler.Handle`'s implicit-else with an explicit three-branch, fail-fast dispatch, adds the `NotSupportedException` → `ErrorCodes.DqtUnsupportedTestType` mapping in the existing catch block, and adds a new fail-fast test using `(DqtTestType)999`.

Each task section below is fully self-contained: it includes the exact current file contents, the exact target code, and the exact test file changes needed, so it can be handed to an isolated developer with no access to any other document.

### task: dqt-job-runner-interface-and-di

## Goal

Introduce a shared `IDqtJobRunner` interface implemented by both `InvoiceDqtJobRunner` and `DriftDqtJobRunner`, register it additively in DI, and add a new `ErrorCodes.DqtUnsupportedTestType = 2204` entry that a later task will consume. This task is purely additive: no existing method body changes, no handler changes. The build must remain green and all existing tests must keep passing unmodified after this task.

## Context

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IInvoiceDqtJobRunner.cs`
```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Runs the invoice DQT comparison for a given DQT run ID.
/// </summary>
public interface IInvoiceDqtJobRunner
{
    Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default);
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDriftDqtJobRunner.cs`
```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IDriftDqtJobRunner
{
    Task RunAsync(Guid runId, CancellationToken ct = default);
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` (full)
```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IInvoiceDqtComparer _comparer;
    private readonly ILogger<InvoiceDqtJobRunner> _logger;

    public InvoiceDqtJobRunner(
        IDqtRunRepository repository,
        IInvoiceDqtComparer comparer,
        ILogger<InvoiceDqtJobRunner> logger)
    {
        _repository = repository;
        _comparer = comparer;
        _logger = logger;
    }

    public async Task RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.GetByIdAsync(dqtRunId, cancellationToken);
        if (run == null)
        {
            _logger.LogWarning("DQT run {DqtRunId} not found", dqtRunId);
            return;
        }

        _logger.LogInformation("Starting DQT run {DqtRunId} ({TestType}) for {DateFrom} to {DateTo}",
            dqtRunId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var result = await _comparer.CompareAsync(run.DateFrom, run.DateTo, cancellationToken);

            var resultEntities = result.Mismatches
                .Select(m => InvoiceDqtResult.Create(run.Id, m.InvoiceCode, m.MismatchType, m.ShoptetValue, m.FlexiValue, m.Details))
                .ToList();

            foreach (var entity in resultEntities)
                run.Results.Add(entity);

            // Explicitly register with EF — FindAsync without Include() does not set up
            // a change-tracking collection, so items added to run.Results are invisible
            // to the context until explicitly added here.
            await _repository.AddResultsAsync(resultEntities, cancellationToken);

            run.Complete(result.TotalChecked, result.Mismatches.Count);

            _logger.LogInformation("DQT run {DqtRunId} completed: {Checked} checked, {Mismatches} mismatches",
                dqtRunId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DQT run {DqtRunId} failed", dqtRunId);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` (full)
```csharp
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtJobRunner : IDriftDqtJobRunner
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly ILogger<DriftDqtJobRunner> _logger;

    public DriftDqtJobRunner(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        ILogger<DriftDqtJobRunner> logger)
    {
        _repository = repository;
        _comparers = comparers;
        _logger = logger;
    }

    public async Task RunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _repository.GetByIdAsync(runId, ct);
        if (run == null)
        {
            _logger.LogWarning("Drift DQT run {RunId} not found", runId);
            return;
        }

        _logger.LogInformation("Starting drift DQT run {RunId} ({TestType}) for {DateFrom} to {DateTo}",
            runId, run.TestType, run.DateFrom, run.DateTo);

        try
        {
            var comparer = _comparers.SingleOrDefault(c => c.TestType == run.TestType)
                ?? throw new InvalidOperationException(
                    $"No IDriftDqtComparer registered for {run.TestType}");

            var result = await comparer.CompareAsync(run.DateFrom, run.DateTo, ct);

            var entities = result.Mismatches
                .Select(m => DqtDriftResult.Create(
                    run.Id, run.TestType, m.EntityKey, m.MismatchCode,
                    m.HebloValue, m.ShoptetValue, m.Details))
                .ToList();

            await _repository.AddDriftResultsAsync(entities, ct);
            run.Complete(result.TotalChecked, result.Mismatches.Count);

            _logger.LogInformation("Drift DQT run {RunId} completed: {Checked} checked, {Mismatches} mismatches",
                runId, result.TotalChecked, result.Mismatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Drift DQT run {RunId} ({TestType}) failed", runId, run.TestType);
            run.Fail(ex.Message);
        }
        finally
        {
            await _repository.SaveChangesAsync(ct);
        }
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` (full)
```csharp
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.DataQuality;

public static class DataQualityModule
{
    public static IServiceCollection AddDataQualityModule(this IServiceCollection services)
    {
        services.AddScoped<IDqtRunRepository, DqtRunRepository>();
        services.AddScoped<IInvoiceDqtComparer, InvoiceDqtComparer>();
        services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
        services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
        services.AddScoped<IDriftDqtComparer, ProductPairingDqtComparer>();
        services.AddScoped<IDriftDqtComparer, StockWriteBackDqtComparer>();

        // Register dashboard tiles
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();

        return services;
    }
}
```

### Current file: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — relevant excerpt (DataQuality `22XX` block, exact lines as they exist today)
```csharp
    // DataQuality module errors (22XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    DqtRunNotFound = 2201,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    DqtInvalidDateRange = 2202,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    DqtExternalServiceError = 2203,

    // Marketing Calendar errors (23XX)
```
The file starts with `using System.Net;` and the enum is `public enum ErrorCodes` in namespace `Anela.Heblo.Application.Shared`. Every entry has an `[HttpStatusCode(...)]` attribute immediately above it (this attribute type is already defined elsewhere in the codebase and does not need to be created).

## Files to create/modify

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs` — **create new file**.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs` — add `IDqtJobRunner` to the class's implemented-interfaces list and add a `CanHandle` method.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs` — add `IDqtJobRunner` to the class's implemented-interfaces list and add a `CanHandle` method.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — add two additive `AddScoped<IDqtJobRunner, ...>()` registrations.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add one new enum entry.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityModuleTests.cs` — **create new file** (DI registration test, see Tests to write).

## Implementation steps

1. Create `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs` with exactly:
   ```csharp
   using Anela.Heblo.Domain.Features.DataQuality;

   namespace Anela.Heblo.Application.Features.DataQuality.Services;

   public interface IDqtJobRunner
   {
       bool CanHandle(DqtTestType testType);
       Task RunAsync(Guid runId, CancellationToken ct = default);
   }
   ```

2. In `InvoiceDqtJobRunner.cs`, change the class declaration line from
   ```csharp
   public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner
   ```
   to
   ```csharp
   public class InvoiceDqtJobRunner : IInvoiceDqtJobRunner, IDqtJobRunner
   ```
   and add this method to the class body (placement: anywhere in the class, e.g. immediately before the existing `RunAsync` method):
   ```csharp
   public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;
   ```
   No other changes to this file. The existing `RunAsync(Guid dqtRunId, CancellationToken cancellationToken = default)` method already satisfies `IDqtJobRunner.RunAsync(Guid runId, CancellationToken ct = default)` — C# does not require parameter names to match between an interface and its implementation.

3. In `DriftDqtJobRunner.cs`, change the class declaration line from
   ```csharp
   public class DriftDqtJobRunner : IDriftDqtJobRunner
   ```
   to
   ```csharp
   public class DriftDqtJobRunner : IDriftDqtJobRunner, IDqtJobRunner
   ```
   and add this method to the class body:
   ```csharp
   public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);
   ```
   This delegates to the already-injected `IEnumerable<IDriftDqtComparer> _comparers` field — no new dependency needed. No other changes to this file.

4. In `DataQualityModule.cs`, add two lines directly beneath the existing `AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>()` / `AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>()` lines, so the block reads:
   ```csharp
   services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
   services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
   services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
   services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
   ```
   Do not remove or modify the two pre-existing lines — this is additive only.

5. In `ErrorCodes.cs`, insert a new entry immediately after `DqtExternalServiceError = 2203,` (still inside the `// DataQuality module errors (22XX)` block, before the `// Marketing Calendar errors (23XX)` comment):
   ```csharp
   [HttpStatusCode(HttpStatusCode.InternalServerError)]
   DqtUnsupportedTestType = 2204,
   ```

## Tests to write

- Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityModuleTests.cs` with a test that verifies the new DI registrations by inspecting `ServiceDescriptor`s on a fresh `IServiceCollection` (do **not** call `BuildServiceProvider()`/resolve instances — `AddDataQualityModule()` also registers a `DbContext`-backed repository and other services that are not trivially constructible in a unit test; inspecting descriptors avoids needing to satisfy all of that). Follow the lightweight pattern already used in `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` (which asserts on `services.Single(s => s.ServiceType == typeof(...))` without building the provider). Exact test file content:
  ```csharp
  using Anela.Heblo.Application.Features.DataQuality;
  using Anela.Heblo.Application.Features.DataQuality.Services;
  using Microsoft.Extensions.DependencyInjection;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.DataQuality;

  public class DataQualityModuleTests
  {
      [Fact]
      public void AddDataQualityModule_RegistersBothRunnersUnderIDqtJobRunner()
      {
          // Arrange
          var services = new ServiceCollection();

          // Act
          services.AddDataQualityModule();

          // Assert
          var dqtJobRunnerDescriptors = services
              .Where(s => s.ServiceType == typeof(IDqtJobRunner))
              .ToList();

          Assert.Equal(2, dqtJobRunnerDescriptors.Count);
          Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(InvoiceDqtJobRunner));
          Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(DriftDqtJobRunner));
      }

      [Fact]
      public void AddDataQualityModule_RetainsExistingNarrowInterfaceRegistrations()
      {
          // Arrange
          var services = new ServiceCollection();

          // Act
          services.AddDataQualityModule();

          // Assert — narrow interfaces are retained, additive-only change
          Assert.Contains(services, d => d.ServiceType == typeof(IInvoiceDqtJobRunner) && d.ImplementationType == typeof(InvoiceDqtJobRunner));
          Assert.Contains(services, d => d.ServiceType == typeof(IDriftDqtJobRunner) && d.ImplementationType == typeof(DriftDqtJobRunner));
      }
  }
  ```
  Note: `AddDataQualityModule` is an extension method in namespace `Anela.Heblo.Application.Features.DataQuality` (the `using` above brings it into scope).

## Acceptance criteria

- `dotnet build` succeeds for the whole solution.
- `dotnet format` reports no changes needed (or is run to apply formatting) for all touched files.
- All pre-existing tests in `Anela.Heblo.Tests` continue to pass unmodified (this task does not touch `RunDqtHandler.cs`, `GetDqtRunDetailHandler.cs`, `RunDqtHandlerTests.cs`, or `GetDqtRunDetailHandlerTests.cs`).
- The two new tests in `DataQualityModuleTests.cs` pass.
- `InvoiceDqtJobRunner.CanHandle(DqtTestType.IssuedInvoiceComparison)` returns `true`; `CanHandle` for `ProductPairing` or `StockWriteBackReconciliation` returns `false`.
- `DriftDqtJobRunner.CanHandle` returns `true` for `ProductPairing` and `StockWriteBackReconciliation`, `false` for `IssuedInvoiceComparison`.
- `ErrorCodes.DqtUnsupportedTestType` exists with value `2204` and `[HttpStatusCode(HttpStatusCode.InternalServerError)]`.

### task: rundqt-handler-dispatch

## Goal

Replace `RunDqtHandler.Handle`'s binary `if (TestType == IssuedInvoiceComparison) {...} else {...}` dispatch (inside the existing fire-and-forget `Task.Run`) with resolution over all registered `IDqtJobRunner`s via `SingleOrDefault(r => r.CanHandle(request.TestType))`, throwing `InvalidOperationException` if none match. Update `RunDqtHandlerTests.cs`'s mock wiring accordingly, since its current setup stubs `IServiceProvider.GetService(typeof(IInvoiceDqtJobRunner))`, which will silently stop being hit once the handler calls `GetServices<IDqtJobRunner>()` instead.

**Prerequisite:** This task requires `IDqtJobRunner` (in `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs`) to already exist, with the signature `bool CanHandle(DqtTestType testType); Task RunAsync(Guid runId, CancellationToken ct = default);` — created by a prior task in this plan. Assume it exists; do not recreate it.

## Context

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` (full, exact)
```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;

public class RunDqtHandler : IRequestHandler<RunDqtRequest, RunDqtResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunDqtHandler> _logger;

    public RunDqtHandler(
        IDqtRunRepository repository,
        IServiceScopeFactory scopeFactory,
        ILogger<RunDqtHandler> logger)
    {
        _repository = repository;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<RunDqtResponse> Handle(RunDqtRequest request, CancellationToken cancellationToken)
    {
        if (request.DateFrom > request.DateTo)
        {
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.DqtInvalidDateRange
            };
        }

        try
        {
            var run = DqtRun.Start(request.TestType, request.DateFrom, request.DateTo, DqtTriggerType.Manual);
            await _repository.AddAsync(run, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            // Fire-and-forget in a dedicated scope — the HTTP request scope is disposed
            // before RunAsync completes, so capturing _jobRunner directly would cause
            // ObjectDisposedException on the DbContext.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                if (request.TestType == DqtTestType.IssuedInvoiceComparison)
                {
                    var runner = scope.ServiceProvider.GetRequiredService<IInvoiceDqtJobRunner>();
                    await runner.RunAsync(run.Id);
                }
                else
                {
                    var runner = scope.ServiceProvider.GetRequiredService<IDriftDqtJobRunner>();
                    await runner.RunAsync(run.Id);
                }
            }, CancellationToken.None);

            _logger.LogInformation("DQT run {DqtRunId} started for {TestType} from {DateFrom} to {DateTo}",
                run.Id, run.TestType, run.DateFrom, run.DateTo);

            return new RunDqtResponse
            {
                DqtRunId = run.Id,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting DQT run");
            return new RunDqtResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
}
```

### Current file: `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` (full, exact — this is what you must rewrite)
```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class RunDqtHandlerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IInvoiceDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly RunDqtHandler _sut;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    public RunDqtHandlerTests()
    {
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IInvoiceDqtJobRunner)))
            .Returns(_jobRunnerMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        _sut = new RunDqtHandler(
            _repositoryMock.Object,
            _scopeFactoryMock.Object,
            NullLogger<RunDqtHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidRequest_SavesRunAndReturnsId()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _jobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.DqtRunId);
        Assert.Null(response.ErrorCode);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DateFromAfterDateTo_ReturnsInvalidDateRangeError()
    {
        // Arrange
        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = To,
            DateTo = From  // swapped
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtInvalidDateRange, response.ErrorCode);
        Assert.Null(response.DqtRunId);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameDateFromAndTo_Succeeds()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _jobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = From  // same date
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.DqtRunId);
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsExceptionError()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.Exception, response.ErrorCode);
        Assert.Null(response.DqtRunId);
    }
}
```

### `DqtTestType` enum (for reference — unchanged, do not modify)
```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

public enum DqtTestType
{
    IssuedInvoiceComparison = 1,
    ProductPairing = 2,
    StockWriteBackReconciliation = 3
}
```

## Files to create/modify

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs` — replace the fire-and-forget `Task.Run` body's dispatch logic.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/RunDqtHandlerTests.cs` — rewrite mock setup and add 3 new test cases.

## Implementation steps

1. In `RunDqtHandler.cs`, replace this block (inside `Handle`, inside `_ = Task.Run(async () => { ... })`):
   ```csharp
                using var scope = _scopeFactory.CreateScope();
                if (request.TestType == DqtTestType.IssuedInvoiceComparison)
                {
                    var runner = scope.ServiceProvider.GetRequiredService<IInvoiceDqtJobRunner>();
                    await runner.RunAsync(run.Id);
                }
                else
                {
                    var runner = scope.ServiceProvider.GetRequiredService<IDriftDqtJobRunner>();
                    await runner.RunAsync(run.Id);
                }
   ```
   with:
   ```csharp
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider
                    .GetServices<IDqtJobRunner>()
                    .SingleOrDefault(r => r.CanHandle(request.TestType))
                    ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
                await runner.RunAsync(run.Id);
   ```
   No other part of `RunDqtHandler.cs` changes — the outer synchronous `try/catch`, the `DqtRun.Start(...)` call, `_repository.AddAsync`/`SaveChangesAsync`, the logging call, and the `RunDqtResponse` construction are all untouched. `GetServices<IDqtJobRunner>()` requires `using Microsoft.Extensions.DependencyInjection;`, already present in the file. `SingleOrDefault` requires LINQ (`System.Linq`) — check whether an explicit `using System.Linq;` is needed (it may already be implicitly available via global usings in this project; if the build fails with a missing `SingleOrDefault` extension method, add `using System.Linq;` to the top of the file).

2. In `RunDqtHandlerTests.cs`, rewrite the file entirely to match the target below (rationale for each change is inline as comments in the code, remove the inline explanatory comments if you prefer — they are here to explain the diff, not required in the final file):

   Replace the field:
   ```csharp
   private readonly Mock<IInvoiceDqtJobRunner> _jobRunnerMock = new();
   ```
   with two fields:
   ```csharp
   private readonly Mock<IDqtJobRunner> _invoiceJobRunnerMock = new();
   private readonly Mock<IDqtJobRunner> _driftJobRunnerMock = new();
   ```

   Replace the constructor body's mock wiring (which stubs `sp.GetService(typeof(IInvoiceDqtJobRunner))`) with wiring for `sp.GetService(typeof(IEnumerable<IDqtJobRunner>))` — this is what `GetServices<IDqtJobRunner>()` resolves under the hood. Set up `CanHandle` on both mocks so that by default the invoice mock claims `IssuedInvoiceComparison` and the drift mock claims everything else (mirroring the real `InvoiceDqtJobRunner`/`DriftDqtJobRunner` behavior); individual tests can override a specific `CanHandle` setup afterward if needed (Moq: the most recently configured matching setup wins).

   Full replacement file content:
   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Services;
   using Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;
   using Anela.Heblo.Application.Shared;
   using Anela.Heblo.Domain.Features.DataQuality;
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Logging.Abstractions;
   using Moq;

   namespace Anela.Heblo.Tests.Features.DataQuality;

   public class RunDqtHandlerTests
   {
       private readonly Mock<IDqtRunRepository> _repositoryMock = new();
       private readonly Mock<IDqtJobRunner> _invoiceJobRunnerMock = new();
       private readonly Mock<IDqtJobRunner> _driftJobRunnerMock = new();
       private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
       private readonly RunDqtHandler _sut;

       private static readonly DateOnly From = new(2026, 1, 1);
       private static readonly DateOnly To = new(2026, 1, 31);

       public RunDqtHandlerTests()
       {
           // Default CanHandle wiring mirrors real InvoiceDqtJobRunner/DriftDqtJobRunner behavior:
           // invoice mock claims IssuedInvoiceComparison only, drift mock claims everything else.
           _invoiceJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.IssuedInvoiceComparison)).Returns(true);
           _invoiceJobRunnerMock
               .Setup(r => r.CanHandle(It.Is<DqtTestType>(t => t != DqtTestType.IssuedInvoiceComparison)))
               .Returns(false);

           _driftJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.IssuedInvoiceComparison)).Returns(false);
           _driftJobRunnerMock
               .Setup(r => r.CanHandle(It.Is<DqtTestType>(t => t != DqtTestType.IssuedInvoiceComparison)))
               .Returns(true);

           var scopeMock = new Mock<IServiceScope>();
           var serviceProviderMock = new Mock<IServiceProvider>();
           serviceProviderMock
               .Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
               .Returns(new List<IDqtJobRunner> { _invoiceJobRunnerMock.Object, _driftJobRunnerMock.Object });
           scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
           _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

           _sut = new RunDqtHandler(
               _repositoryMock.Object,
               _scopeFactoryMock.Object,
               NullLogger<RunDqtHandler>.Instance);
       }

       [Fact]
       public async Task Handle_ValidRequest_SavesRunAndReturnsId()
       {
           // Arrange
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DqtRun run, CancellationToken _) => run);
           _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.IssuedInvoiceComparison,
               DateFrom = From,
               DateTo = To
           };

           // Act
           var response = await _sut.Handle(request, CancellationToken.None);

           // Assert
           Assert.True(response.Success);
           Assert.NotNull(response.DqtRunId);
           Assert.Null(response.ErrorCode);
           _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Once);
       }

       [Fact]
       public async Task Handle_DateFromAfterDateTo_ReturnsInvalidDateRangeError()
       {
           // Arrange
           var request = new RunDqtRequest
           {
               TestType = DqtTestType.IssuedInvoiceComparison,
               DateFrom = To,
               DateTo = From  // swapped
           };

           // Act
           var response = await _sut.Handle(request, CancellationToken.None);

           // Assert
           Assert.False(response.Success);
           Assert.Equal(ErrorCodes.DqtInvalidDateRange, response.ErrorCode);
           Assert.Null(response.DqtRunId);
           _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
       }

       [Fact]
       public async Task Handle_SameDateFromAndTo_Succeeds()
       {
           // Arrange
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DqtRun run, CancellationToken _) => run);
           _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.IssuedInvoiceComparison,
               DateFrom = From,
               DateTo = From  // same date
           };

           // Act
           var response = await _sut.Handle(request, CancellationToken.None);

           // Assert
           Assert.True(response.Success);
           Assert.NotNull(response.DqtRunId);
       }

       [Fact]
       public async Task Handle_RepositoryThrows_ReturnsExceptionError()
       {
           // Arrange
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new Exception("DB error"));

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.IssuedInvoiceComparison,
               DateFrom = From,
               DateTo = To
           };

           // Act
           var response = await _sut.Handle(request, CancellationToken.None);

           // Assert
           Assert.False(response.Success);
           Assert.Equal(ErrorCodes.Exception, response.ErrorCode);
           Assert.Null(response.DqtRunId);
       }

       [Fact]
       public async Task Handle_InvoiceTestType_InvokesMatchingRunnerOnly()
       {
           // Arrange
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DqtRun run, CancellationToken _) => run);
           _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.IssuedInvoiceComparison,
               DateFrom = From,
               DateTo = To
           };

           // Act
           await _sut.Handle(request, CancellationToken.None);
           await Task.Delay(100); // allow the fire-and-forget Task.Run to execute

           // Assert
           _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
           _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
       }

       [Fact]
       public async Task Handle_DriftTestType_InvokesMatchingRunnerOnly()
       {
           // Arrange
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DqtRun run, CancellationToken _) => run);
           _driftJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.ProductPairing,
               DateFrom = From,
               DateTo = To
           };

           // Act
           await _sut.Handle(request, CancellationToken.None);
           await Task.Delay(100); // allow the fire-and-forget Task.Run to execute

           // Assert
           _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
           _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
       }

       [Fact]
       public async Task Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked()
       {
           // Arrange: simulate "no IDqtJobRunner registered for this TestType" by making both
           // mocks explicitly reject StockWriteBackReconciliation (overrides the constructor's
           // default wiring — Moq uses the most recently configured matching setup).
           _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DqtRun run, CancellationToken _) => run);
           _invoiceJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);
           _driftJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);

           var request = new RunDqtRequest
           {
               TestType = DqtTestType.StockWriteBackReconciliation,
               DateFrom = From,
               DateTo = To
           };

           // Act
           var response = await _sut.Handle(request, CancellationToken.None);
           await Task.Delay(100); // allow the fire-and-forget Task.Run to throw internally

           // Assert: Handle() itself still succeeds — the InvalidOperationException is thrown
           // inside the fire-and-forget Task.Run and is not observed by the caller. This is a
           // pre-existing, out-of-scope characteristic of the fire-and-forget design, not a
           // regression introduced by this change. We can only assert that neither runner ran.
           Assert.True(response.Success);
           _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
           _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
       }
   }
   ```

## Tests to write

(All included verbatim in step 2 above — summarized:)
- `Handle_InvoiceTestType_InvokesMatchingRunnerOnly` — `TestType = IssuedInvoiceComparison` invokes the invoice-runner mock's `RunAsync` exactly once and never invokes the drift-runner mock.
- `Handle_DriftTestType_InvokesMatchingRunnerOnly` — `TestType = ProductPairing` invokes the drift-runner mock's `RunAsync` exactly once and never invokes the invoice-runner mock.
- `Handle_NoRunnerCanHandleTestType_NeitherRunnerInvoked` — when no mock's `CanHandle` returns `true` for the given `TestType`, neither runner's `RunAsync` is invoked, and `Handle`'s own return value is unaffected (`Success = true`, since the fire-and-forget task's `InvalidOperationException` is not observed by the caller — pre-existing behavior, not changed by this task).
- All 4 pre-existing tests (`Handle_ValidRequest_SavesRunAndReturnsId`, `Handle_DateFromAfterDateTo_ReturnsInvalidDateRangeError`, `Handle_SameDateFromAndTo_Succeeds`, `Handle_RepositoryThrows_ReturnsExceptionError`) continue to pass with the same assertions, only the mock field name changed (`_jobRunnerMock` → `_invoiceJobRunnerMock`).

## Acceptance criteria

- `dotnet build` succeeds.
- `dotnet format` reports no changes needed (or is run to apply formatting).
- All 7 tests in `RunDqtHandlerTests.cs` pass.
- For `request.TestType == DqtTestType.IssuedInvoiceComparison`, only the invoice runner's `RunAsync` is invoked.
- For `request.TestType` equal to `ProductPairing` or `StockWriteBackReconciliation`, only the drift runner's `RunAsync` is invoked.
- `SingleOrDefault` (not `FirstOrDefault`) is used in the new dispatch code.
- No other behavior of `RunDqtHandler.Handle` changes (date-range validation, run creation, response shape, exception handling in the outer `try/catch`).

### task: getdqtrundetail-handler-dispatch

## Goal

Replace `GetDqtRunDetailHandler.Handle`'s implicit-else result-shaping dispatch (`if (invoice) return invoice-shaped;` followed by an unconditional fallthrough to drift-shaped, with no `else`) with an explicit three-branch dispatch that throws `NotSupportedException` for any unrecognized `DqtTestType`. Map that exception to the new `ErrorCodes.DqtUnsupportedTestType` (value `2204`) in the handler's existing outer `catch` block. Add a new test asserting the fail-fast path using `(DqtTestType)999`.

**Prerequisite:** This task requires `ErrorCodes.DqtUnsupportedTestType = 2204` to already exist in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (with `[HttpStatusCode(HttpStatusCode.InternalServerError)]`) — created by a prior task in this plan. Assume it exists; do not recreate it.

## Context

### Current file: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` (full, exact)
```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;

public class GetDqtRunDetailHandler : IRequestHandler<GetDqtRunDetailRequest, GetDqtRunDetailResponse>
{
    private readonly IDqtRunRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetDqtRunDetailHandler> _logger;

    public GetDqtRunDetailHandler(IDqtRunRepository repository, IMapper mapper, ILogger<GetDqtRunDetailHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetDqtRunDetailResponse> Handle(GetDqtRunDetailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _repository.GetWithResultsAsync(request.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

            if (run == null)
            {
                return new GetDqtRunDetailResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.DqtRunNotFound
                };
            }

            if (run.TestType == DqtTestType.IssuedInvoiceComparison)
            {
                return new GetDqtRunDetailResponse
                {
                    Success = true,
                    Run = _mapper.Map<DqtRunDto>(run),
                    Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
                };
            }

            var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
                run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

            return new GetDqtRunDetailResponse
            {
                Success = true,
                Run = _mapper.Map<DqtRunDto>(run),
                DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
                TotalDriftResults = driftTotal
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
            return new GetDqtRunDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception
            };
        }
    }
}
```

### Current file: `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` (full, exact — this is what you must modify)
```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class GetDqtRunDetailHandlerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetDqtRunDetailHandler _sut;

    public GetDqtRunDetailHandlerTests()
    {
        _sut = new GetDqtRunDetailHandler(_repositoryMock.Object, _mapperMock.Object, NullLogger<GetDqtRunDetailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_RunNotFound_ReturnsNotFoundError()
    {
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        var request = new GetDqtRunDetailRequest { Id = id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtRunNotFound, response.ErrorCode);
        Assert.Null(response.Run);
    }

    [Fact]
    public async Task Handle_RunExists_ReturnsMappedDetail()
    {
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual);
        var dto = new DqtRunDto { Id = run.Id };
        var resultDtos = new List<InvoiceDqtResultDto>();

        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _mapperMock
            .Setup(m => m.Map<DqtRunDto>(run))
            .Returns(dto);

        _mapperMock
            .Setup(m => m.Map<List<InvoiceDqtResultDto>>(run.Results))
            .Returns(resultDtos);

        var request = new GetDqtRunDetailRequest { Id = run.Id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Run);
        Assert.Equal(run.Id, response.Run.Id);
        Assert.Null(response.ErrorCode);
    }
}
```

### `DqtRun.Start` factory (for reference — unchanged, do not modify; confirms no enum validation happens here, so `(DqtTestType)999` can be passed through freely for test purposes)
```csharp
// backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtRun.cs
public static DqtRun Start(DqtTestType testType, DateOnly dateFrom, DateOnly dateTo, DqtTriggerType triggerType)
{
    return new DqtRun
    {
        Id = Guid.NewGuid(),
        TestType = testType,
        DateFrom = dateFrom,
        DateTo = dateTo,
        Status = DqtRunStatus.Running,
        StartedAt = DateTime.UtcNow,
        TriggerType = triggerType
    };
}
```

### `DqtTestType` enum (for reference — unchanged, do not modify)
```csharp
namespace Anela.Heblo.Domain.Features.DataQuality;

public enum DqtTestType
{
    IssuedInvoiceComparison = 1,
    ProductPairing = 2,
    StockWriteBackReconciliation = 3
}
```

## Files to create/modify

- `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` — replace the implicit-else dispatch with explicit three-branch fail-fast dispatch; update the outer `catch` block's `ErrorCode` mapping.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` — add one new test for the fail-fast path.

## Implementation steps

1. In `GetDqtRunDetailHandler.cs`, replace this block (everything between the `run == null` check and the outer `catch`):
   ```csharp
               if (run.TestType == DqtTestType.IssuedInvoiceComparison)
               {
                   return new GetDqtRunDetailResponse
                   {
                       Success = true,
                       Run = _mapper.Map<DqtRunDto>(run),
                       Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
                   };
               }

               var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
                   run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

               return new GetDqtRunDetailResponse
               {
                   Success = true,
                   Run = _mapper.Map<DqtRunDto>(run),
                   DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
                   TotalDriftResults = driftTotal
               };
   ```
   with:
   ```csharp
               if (run.TestType == DqtTestType.IssuedInvoiceComparison)
               {
                   return new GetDqtRunDetailResponse
                   {
                       Success = true,
                       Run = _mapper.Map<DqtRunDto>(run),
                       Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
                   };
               }

               if (run.TestType is DqtTestType.ProductPairing or DqtTestType.StockWriteBackReconciliation)
               {
                   var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
                       run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

                   return new GetDqtRunDetailResponse
                   {
                       Success = true,
                       Run = _mapper.Map<DqtRunDto>(run),
                       DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
                       TotalDriftResults = driftTotal
                   };
               }

               throw new NotSupportedException($"No result-shaping logic registered for DqtTestType {run.TestType}");
   ```
   (Indentation above matches the file's existing 12-space indentation inside the `try` block — adjust to match exactly what your editor shows for the surrounding lines.)

2. Replace the outer `catch` block:
   ```csharp
       catch (Exception ex)
       {
           _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
           return new GetDqtRunDetailResponse
           {
               Success = false,
               ErrorCode = ErrorCodes.Exception
           };
       }
   ```
   with:
   ```csharp
       catch (Exception ex)
       {
           _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
           return new GetDqtRunDetailResponse
           {
               Success = false,
               ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception
           };
       }
   ```
   No new nested `try/catch` is introduced — the `NotSupportedException` thrown in step 1 propagates naturally to this existing outer `catch (Exception ex)`, which now distinguishes it via `ex is NotSupportedException`.

3. No other changes to this file. `run == null` handling, constructor, field declarations, and using directives are all unchanged (the `NotSupportedException` type is in `System`, which does not need an explicit `using` in C# — it is implicitly available; do not add a `using System;` unless the build actually requires it).

4. In `GetDqtRunDetailHandlerTests.cs`, add one new test method inside the `GetDqtRunDetailHandlerTests` class, after the existing `Handle_RunExists_ReturnsMappedDetail` test:
   ```csharp
       [Fact]
       public async Task Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError()
       {
           // (DqtTestType)999 is an explicit out-of-range cast — no such DqtTestType value exists
           // today. This is the standard way to test an enum-dispatch fail-fast path without
           // modifying the DqtTestType enum itself.
           var run = DqtRun.Start((DqtTestType)999, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual);

           _repositoryMock
               .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

           var request = new GetDqtRunDetailRequest { Id = run.Id };

           var response = await _sut.Handle(request, CancellationToken.None);

           Assert.False(response.Success);
           Assert.Equal(ErrorCodes.DqtUnsupportedTestType, response.ErrorCode);
           Assert.Null(response.Run);
       }
   ```
   No changes are needed to the two existing tests (`Handle_RunNotFound_ReturnsNotFoundError`, `Handle_RunExists_ReturnsMappedDetail`) — `IssuedInvoiceComparison` still hits the first `if` branch exactly as before, and the `run == null` branch is unaffected by this task.

## Tests to write

- `Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError` (full content in step 4 above): a `DqtRun` constructed with `(DqtTestType)999` results in `Handle` returning `Success = false` and `ErrorCode = ErrorCodes.DqtUnsupportedTestType`, not a partially-populated success response and not an unhandled exception escaping `Handle`.

## Acceptance criteria

- `dotnet build` succeeds.
- `dotnet format` reports no changes needed (or is run to apply formatting).
- All 3 tests in `GetDqtRunDetailHandlerTests.cs` pass (the 2 pre-existing tests plus the 1 new one).
- `run.TestType == DqtTestType.IssuedInvoiceComparison` still returns the invoice-shaped response (`Results` populated, `DriftResults`/`TotalDriftResults` left at their default values).
- `run.TestType` equal to `ProductPairing` or `StockWriteBackReconciliation` still returns the drift-shaped response (`DriftResults`/`TotalDriftResults` populated, `Results` left at its default value).
- Any other `DqtTestType` value results in `Success = false, ErrorCode = ErrorCodes.DqtUnsupportedTestType` (not `ErrorCodes.Exception`, and not an unhandled exception).
- The thrown exception type inside `Handle` for the fail-fast path is `NotSupportedException`, with a message identifying the unhandled `TestType` value.

### Final validation (run after all 3 tasks are complete)

Once all three tasks above are implemented in order, run the full DataQuality test subset to confirm no regressions across the slice:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.DataQuality"
```
This should cover `DataQualityModuleTests`, `RunDqtHandlerTests`, `GetDqtRunDetailHandlerTests`, and any other existing DataQuality tests (e.g. `InvoiceDqtJobTests`, `ProductPairingDqtJobTests`, `StockWriteBackDqtJobTests`, which are unaffected by these changes since `IInvoiceDqtJobRunner`/`IDriftDqtJobRunner` are retained unchanged). Also run `dotnet build` and `dotnet format` for the whole solution one final time to confirm a clean state.
