# GetDqtRunDetailHandler Result-Shaping Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two `if (run.TestType == ...)` blocks and the `throw new NotSupportedException(...)` fallback in `GetDqtRunDetailHandler` with a polymorphic `IDqtResultShaper` lookup, so adding a future `DqtTestType` never requires editing this handler again.

**Architecture:** Two new, single-purpose classes (`InvoiceDqtResultShaper`, `DriftDqtResultShaper`) implement a new `IDqtResultShaper` interface (`CanHandle(DqtTestType)` + `ShapeAsync(run, response, page, pageSize, ct)`), registered in `DataQualityModule`. `GetDqtRunDetailHandler` resolves the matching shaper via `IEnumerable<IDqtResultShaper>.SingleOrDefault(s => s.CanHandle(run.TestType))` and delegates to it instead of branching on the enum itself. The existing `InvoiceDqtJobRunner`/`DriftDqtJobRunner` classes and their tests are **not** touched.

**Tech Stack:** .NET 8, MediatR, AutoMapper, xUnit, Moq.

---

### task: implement-invoice-result-shaper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class InvoiceDqtResultShaperTests
{
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly InvoiceDqtResultShaper _sut;

    public InvoiceDqtResultShaperTests()
    {
        _sut = new InvoiceDqtResultShaper(_mapperMock.Object);
    }

    private static DqtRun CreateRun(DqtTestType testType)
        => DqtRun.Start(testType, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);

    [Theory]
    [InlineData(DqtTestType.IssuedInvoiceComparison, true)]
    [InlineData(DqtTestType.ProductPairing, false)]
    [InlineData(DqtTestType.StockWriteBackReconciliation, false)]
    [InlineData(DqtTestType.LotSumVsErpStock, false)]
    [InlineData(DqtTestType.PriceComparison, false)]
    public void CanHandle_ReturnsTrueOnlyForIssuedInvoiceComparison(DqtTestType testType, bool expected)
    {
        Assert.Equal(expected, _sut.CanHandle(testType));
    }

    [Fact]
    public async Task ShapeAsync_MapsRunResultsOntoResponse_AndLeavesDriftFieldsUntouched()
    {
        // Arrange
        var run = CreateRun(DqtTestType.IssuedInvoiceComparison);
        var response = new GetDqtRunDetailResponse { Success = true };
        var mapped = new List<InvoiceDqtResultDto> { new() { Id = Guid.NewGuid(), InvoiceCode = "INV-001" } };

        _mapperMock
            .Setup(m => m.Map<List<InvoiceDqtResultDto>>(run.Results))
            .Returns(mapped);

        // Act
        await _sut.ShapeAsync(run, response, page: 1, pageSize: 50, ct: CancellationToken.None);

        // Assert
        Assert.Same(mapped, response.Results);
        Assert.Null(response.DriftResults);
        Assert.Equal(0, response.TotalDriftResults);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~InvoiceDqtResultShaperTests`
Expected: FAIL to compile — `InvoiceDqtResultShaper` and `IDqtResultShaper` do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Populates the result portion (Results / DriftResults / TotalDriftResults) of a
/// GetDqtRunDetailResponse for a given DqtRun's TestType. Implementations must not set
/// Run, Success, or ErrorCode on the response — those remain owned by
/// GetDqtRunDetailHandler, which resolves the matching IDqtResultShaper via CanHandle
/// before calling ShapeAsync, exactly like IDqtJobRunner is resolved in RunDqtHandler.
/// </summary>
public interface IDqtResultShaper
{
    bool CanHandle(DqtTestType testType);

    Task ShapeAsync(
        DqtRun run,
        GetDqtRunDetailResponse response,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
```

Create `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtResultShaper : IDqtResultShaper
{
    private readonly IMapper _mapper;

    public InvoiceDqtResultShaper(IMapper mapper)
    {
        _mapper = mapper;
    }

    public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;

    public Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        response.Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~InvoiceDqtResultShaperTests`
Expected: PASS (6 tests: 5 `CanHandle` theory cases + 1 `ShapeAsync` fact).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs
git commit -m "feat(dataquality): add IDqtResultShaper and InvoiceDqtResultShaper"
```

---

### task: implement-drift-result-shaper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtResultShaperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtResultShaperTests.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DriftDqtResultShaperTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IMapper> _mapperMock = new();

    private static DqtRun CreateRun(DqtTestType testType)
        => DqtRun.Start(testType, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);

    private DriftDqtResultShaper CreateSut(params DqtTestType[] handledTypes)
    {
        var comparers = handledTypes.Select(t =>
        {
            var mock = new Mock<IDriftDqtComparer>();
            mock.SetupGet(c => c.TestType).Returns(t);
            return mock.Object;
        }).ToList();

        return new DriftDqtResultShaper(_repositoryMock.Object, comparers, _mapperMock.Object);
    }

    [Theory]
    [InlineData(DqtTestType.ProductPairing, true)]
    [InlineData(DqtTestType.StockWriteBackReconciliation, true)]
    [InlineData(DqtTestType.LotSumVsErpStock, true)]
    [InlineData(DqtTestType.PriceComparison, true)]
    [InlineData(DqtTestType.IssuedInvoiceComparison, false)]
    public void CanHandle_DerivesFromInjectedComparers_NotAHardcodedList(DqtTestType testType, bool expected)
    {
        // Arrange — sut only knows about the 4 drift types via its injected comparers,
        // exactly like DriftDqtJobRunner.CanHandle
        var sut = CreateSut(
            DqtTestType.ProductPairing,
            DqtTestType.StockWriteBackReconciliation,
            DqtTestType.LotSumVsErpStock,
            DqtTestType.PriceComparison);

        // Act & Assert
        Assert.Equal(expected, sut.CanHandle(testType));
    }

    [Fact]
    public async Task ShapeAsync_MapsDriftResultsAndTotalOntoResponse_AndLeavesResultsUntouched()
    {
        // Arrange
        var sut = CreateSut(DqtTestType.ProductPairing);
        var run = CreateRun(DqtTestType.ProductPairing);
        var response = new GetDqtRunDetailResponse { Success = true };
        var driftItems = new List<DqtDriftResult>();
        var mappedDrift = new List<DqtDriftResultDto> { new() { EntityKey = "SKU-1", MismatchCode = 1 } };

        _repositoryMock
            .Setup(r => r.GetDriftResultsAsync(run.Id, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((driftItems, 7));

        _mapperMock
            .Setup(m => m.Map<List<DqtDriftResultDto>>(driftItems))
            .Returns(mappedDrift);

        // Act
        await sut.ShapeAsync(run, response, page: 2, pageSize: 25, ct: CancellationToken.None);

        // Assert
        Assert.Same(mappedDrift, response.DriftResults);
        Assert.Equal(7, response.TotalDriftResults);
        Assert.Empty(response.Results);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DriftDqtResultShaperTests`
Expected: FAIL to compile — `DriftDqtResultShaper` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs`:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Domain.Features.DataQuality;
using AutoMapper;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class DriftDqtResultShaper : IDqtResultShaper
{
    private readonly IDqtRunRepository _repository;
    private readonly IEnumerable<IDriftDqtComparer> _comparers;
    private readonly IMapper _mapper;

    public DriftDqtResultShaper(
        IDqtRunRepository repository,
        IEnumerable<IDriftDqtComparer> comparers,
        IMapper mapper)
    {
        _repository = repository;
        _comparers = comparers;
        _mapper = mapper;
    }

    public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);

    public async Task ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)
    {
        var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(run.Id, page, pageSize, ct);
        response.DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems);
        response.TotalDriftResults = driftTotal;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DriftDqtResultShaperTests`
Expected: PASS (5 `CanHandle` theory cases + 1 `ShapeAsync` fact).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtResultShaper.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/DriftDqtResultShaperTests.cs
git commit -m "feat(dataquality): add DriftDqtResultShaper"
```

---

### task: rewire-handler-and-register-shapers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs:10-74`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs`

- [ ] **Step 1: Update the existing tests to mock `IEnumerable<IDqtResultShaper>` instead of the `if` chain**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
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
    private readonly Mock<IDqtResultShaper> _shaperMock = new();
    private readonly GetDqtRunDetailHandler _sut;

    public GetDqtRunDetailHandlerTests()
    {
        _sut = new GetDqtRunDetailHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            new[] { _shaperMock.Object },
            NullLogger<GetDqtRunDetailHandler>.Instance);
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
        var run = DqtRun.Start(DqtTestType.IssuedInvoiceComparison, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);
        var dto = new DqtRunDto { Id = run.Id };
        var resultDtos = new List<InvoiceDqtResultDto>();

        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _mapperMock
            .Setup(m => m.Map<DqtRunDto>(run))
            .Returns(dto);

        _shaperMock.Setup(s => s.CanHandle(DqtTestType.IssuedInvoiceComparison)).Returns(true);
        _shaperMock
            .Setup(s => s.ShapeAsync(run, It.IsAny<GetDqtRunDetailResponse>(), 1, 50, It.IsAny<CancellationToken>()))
            .Callback<DqtRun, GetDqtRunDetailResponse, int, int, CancellationToken>((_, response, _, _, _) => response.Results = resultDtos)
            .Returns(Task.CompletedTask);

        var request = new GetDqtRunDetailRequest { Id = run.Id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Run);
        Assert.Equal(run.Id, response.Run.Id);
        Assert.Same(resultDtos, response.Results);
        Assert.Null(response.ErrorCode);
    }

    [Theory]
    [InlineData(DqtTestType.ProductPairing)]
    [InlineData(DqtTestType.StockWriteBackReconciliation)]
    [InlineData(DqtTestType.LotSumVsErpStock)]
    [InlineData(DqtTestType.PriceComparison)]
    public async Task Handle_DriftTestType_ReturnsMappedDriftResults(DqtTestType testType)
    {
        var run = DqtRun.Start(testType, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);
        var dto = new DqtRunDto { Id = run.Id };
        var driftDtos = new List<DqtDriftResultDto>();

        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _mapperMock
            .Setup(m => m.Map<DqtRunDto>(run))
            .Returns(dto);

        _shaperMock.Setup(s => s.CanHandle(testType)).Returns(true);
        _shaperMock
            .Setup(s => s.ShapeAsync(run, It.IsAny<GetDqtRunDetailResponse>(), 1, 50, It.IsAny<CancellationToken>()))
            .Callback<DqtRun, GetDqtRunDetailResponse, int, int, CancellationToken>((_, response, _, _, _) =>
            {
                response.DriftResults = driftDtos;
                response.TotalDriftResults = 7;
            })
            .Returns(Task.CompletedTask);

        var request = new GetDqtRunDetailRequest { Id = run.Id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Run);
        Assert.Same(driftDtos, response.DriftResults);
        Assert.Equal(7, response.TotalDriftResults);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError()
    {
        // (DqtTestType)999 is an explicit out-of-range cast — no such DqtTestType value exists
        // today. This is the standard way to test an enum-dispatch fail-fast path without
        // modifying the DqtTestType enum itself. No registered shaper's CanHandle matches it
        // (the default Mock<IDqtResultShaper> returns false for CanHandle), so the handler
        // must return the unsupported-type error without calling ShapeAsync.
        var run = DqtRun.Start((DqtTestType)999, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DqtTriggerType.Manual, DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetWithResultsAsync(run.Id, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var request = new GetDqtRunDetailRequest { Id = run.Id };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtUnsupportedTestType, response.ErrorCode);
        Assert.Null(response.Run);
        _shaperMock.Verify(
            s => s.ShapeAsync(It.IsAny<DqtRun>(), It.IsAny<GetDqtRunDetailResponse>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetDqtRunDetailHandlerTests`
Expected: FAIL to compile — `GetDqtRunDetailHandler`'s constructor does not yet accept `IEnumerable<IDqtResultShaper>`.

- [ ] **Step 3: Rewrite `GetDqtRunDetailHandler`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
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
    private readonly IEnumerable<IDqtResultShaper> _shapers;
    private readonly ILogger<GetDqtRunDetailHandler> _logger;

    public GetDqtRunDetailHandler(
        IDqtRunRepository repository,
        IMapper mapper,
        IEnumerable<IDqtResultShaper> shapers,
        ILogger<GetDqtRunDetailHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _shapers = shapers;
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

            var shaper = _shapers.SingleOrDefault(s => s.CanHandle(run.TestType));
            if (shaper == null)
            {
                return new GetDqtRunDetailResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.DqtUnsupportedTestType
                };
            }

            var response = new GetDqtRunDetailResponse
            {
                Success = true,
                Run = _mapper.Map<DqtRunDto>(run)
            };

            await shaper.ShapeAsync(run, response, request.ResultPage, request.ResultPageSize, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
            return new GetDqtRunDetailResponse
            {
                Success = false,
                ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception
            };
        }
    }
}
```

- [ ] **Step 4: Register the new shapers in `DataQualityModule`**

In `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`, find:

```csharp
        services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
        services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
```

and replace it with:

```csharp
        services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
        services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
        services.AddScoped<IDqtResultShaper, InvoiceDqtResultShaper>();
        services.AddScoped<IDqtResultShaper, DriftDqtResultShaper>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DataQuality`
Expected: PASS — all `GetDqtRunDetailHandlerTests`, `InvoiceDqtResultShaperTests`, `DriftDqtResultShaperTests`, `InvoiceDqtJobRunnerTests`, and `DriftDqtJobRunnerTests` green.

- [ ] **Step 6: Full backend build and format check**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports violations, run `dotnet format` (no `--verify-no-changes`) and re-stage the affected files before committing.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/GetDqtRunDetailHandlerTests.cs
git commit -m "refactor(dataquality): dispatch GetDqtRunDetailHandler via IDqtResultShaper instead of a TestType switch"
```

---

## Self-Review (performed by the planner; recorded for the implementer's confidence)

**1. Spec coverage:**
- FR-1 (introduce `IDqtResultShaper`) → `implement-invoice-result-shaper` Step 3.
- FR-2 (invoice shaper) → `implement-invoice-result-shaper` (whole task).
- FR-3 (drift shaper, no hardcoded enum list) → `implement-drift-result-shaper` (whole task; `CanHandle` derives from `_comparers`, verified by `CanHandle_DerivesFromInjectedComparers_NotAHardcodedList`).
- FR-4 (rewrite handler, preserve `ErrorCodes.DqtUnsupportedTestType` and `Run == null` on the unsupported path) → `rewire-handler-and-register-shapers` Steps 1–3; preserved explicitly by `Handle_UnrecognizedTestType_ReturnsUnsupportedTestTypeError`.
- FR-5 (DI registration) → `rewire-handler-and-register-shapers` Step 4.
- NFR-1 (behavioral parity) → all four existing `GetDqtRunDetailHandlerTests` cases are preserved with unchanged assertions, only setup changed to mock the shaper instead of the repository/mapper calls the handler no longer makes directly.
- NFR-2 (extensibility) → satisfied by construction: a 6th `DqtTestType` needs one new `IDqtResultShaper` implementation + one DI line, zero handler changes.

**2. Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N" placeholders — every step has complete, runnable code.

**3. Type consistency:** `IDqtResultShaper.ShapeAsync(DqtRun run, GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)` signature is identical across `IDqtResultShaper.cs`, `InvoiceDqtResultShaper.cs`, `DriftDqtResultShaper.cs`, the handler's call site, and every test's mock setup/callback.
