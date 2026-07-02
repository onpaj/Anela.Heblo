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

