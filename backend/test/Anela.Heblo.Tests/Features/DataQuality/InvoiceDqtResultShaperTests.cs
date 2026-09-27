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
