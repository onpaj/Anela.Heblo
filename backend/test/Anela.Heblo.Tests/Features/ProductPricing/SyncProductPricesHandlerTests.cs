using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SyncProductPricesHandlerTests
{
    private readonly Mock<IPriceComparisonService> _comparisonService = new();

    private SyncProductPricesHandler CreateHandler() => new(_comparisonService.Object);

    [Fact]
    public async Task returns_the_freshly_read_rows_for_the_requested_products()
    {
        // Arrange
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceComparisonResult
            {
                Rows = new List<PriceDivergenceRowDto>
                {
                    new() { ProductCode = "A", Kind = PriceDivergenceKind.InAgreement },
                },
            });

        // Act
        var response = await CreateHandler().Handle(
            new SyncProductPricesRequest { ProductCodes = new List<string> { "A" } },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Rows.Should().ContainSingle().Which.ProductCode.Should().Be("A");
    }

    [Fact]
    public async Task passes_the_requested_codes_through_to_the_comparison_service()
    {
        // Arrange
        IReadOnlyCollection<string>? received = null;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((codes, _) => received = codes)
            .ReturnsAsync(new PriceComparisonResult());

        // Act
        await CreateHandler().Handle(
            new SyncProductPricesRequest { ProductCodes = new List<string> { "A", "B" } },
            CancellationToken.None);

        // Assert
        received.Should().BeEquivalentTo(new[] { "A", "B" });
    }
}
