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
