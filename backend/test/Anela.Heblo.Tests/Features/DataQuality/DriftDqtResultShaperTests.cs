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
