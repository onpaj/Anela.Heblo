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

