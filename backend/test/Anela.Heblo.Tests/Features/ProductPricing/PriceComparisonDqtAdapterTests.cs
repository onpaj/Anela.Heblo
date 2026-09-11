using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Infrastructure;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class PriceComparisonDqtAdapterTests
{
    private readonly Mock<IPriceComparisonService> _comparisonService = new();

    private PriceComparisonDqtAdapter CreateSut() => new(_comparisonService.Object);

    private void SetupReport(params PriceDivergenceRowDto[] rows) =>
        _comparisonService
            .Setup(s => s.BuildReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriceComparisonResult { Rows = rows.ToList() });

    [Theory]
    [InlineData(PriceDivergenceKind.FlexiDiffers, true)]
    [InlineData(PriceDivergenceKind.MissingInFlexi, true)]
    [InlineData(PriceDivergenceKind.FlexiPriceTypeUnknown, true)]
    [InlineData(PriceDivergenceKind.InAgreement, false)]
    [InlineData(PriceDivergenceKind.MissingInShoptet, false)]
    public async Task GetDivergencesAsync_ClassifiesEachKindAsMismatchOrNot(PriceDivergenceKind kind, bool expectedIsMismatch)
    {
        // Arrange
        SetupReport(new PriceDivergenceRowDto
        {
            ProductCode = "A",
            ShoptetPriceWithVat = 190m,
            FlexiPriceWithVat = 200m,
            Kind = kind,
        });

        // Act
        var result = await CreateSut().GetDivergencesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle().Which.IsMismatch.Should().Be(expectedIsMismatch);
    }

    [Fact]
    public async Task GetDivergencesAsync_CarriesProductCodePricesAndKindThroughFaithfully()
    {
        // Arrange
        SetupReport(new PriceDivergenceRowDto
        {
            ProductCode = "B",
            ShoptetPriceWithVat = 250m,
            FlexiPriceWithVat = 200m,
            Kind = PriceDivergenceKind.FlexiDiffers,
        });

        // Act
        var result = await CreateSut().GetDivergencesAsync(CancellationToken.None);

        // Assert
        var divergence = result.Single();
        divergence.ProductCode.Should().Be("B");
        divergence.ShoptetPriceWithVat.Should().Be(250m);
        divergence.FlexiPriceWithVat.Should().Be(200m);
        divergence.Kind.Should().Be("FlexiDiffers");
    }
}
