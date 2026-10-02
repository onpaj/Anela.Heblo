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

