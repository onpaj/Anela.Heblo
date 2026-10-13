using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class VatRateCalculatorTests
{
    [Theory]
    // FR-1: priceWithoutVat <= 0 falls back to the standard rate, regardless of priceWithVat.
    [InlineData(100, 0, 21)]
    [InlineData(100, -10, 21)]
    // FR-2: formula branch reaching the standard rate (121/100 - 1 = 0.21 -> 21%).
    [InlineData(121, 100, 21)]
    // FR-3: formula branch computing a reduced rate (115/100 - 1 = 0.15 -> 15%).
    [InlineData(115, 100, 15)]
    // FR-4: rounding edge. (211/200 - 1) * 100 = 5.5 exactly. Math.Round(5.5m, 0) uses
    // MidpointRounding.ToEven (banker's rounding) by default, so 5.5 rounds to 6 (the
    // nearest even integer), not 5. This value was verified against .NET's actual
    // Math.Round(decimal, int) semantics, not hand-derived by assumption.
    [InlineData(211, 200, 6)]
    public void FromPrices_ReturnsExpectedVatRate(decimal priceWithVat, decimal priceWithoutVat, decimal expected)
    {
        // Act
        var result = VatRateCalculator.FromPrices(priceWithVat, priceWithoutVat);

        // Assert
        result.Should().Be(expected);
    }
}
