using Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SyncProductPricesRequestValidatorTests
{
    private readonly SyncProductPricesRequestValidator _validator = new();

    [Fact]
    public void accepts_a_request_carrying_product_codes()
    {
        // Arrange
        var request = new SyncProductPricesRequest { ProductCodes = new List<string> { "A", "B" } };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// An empty list would otherwise mean "the whole catalogue", which is exactly the
    /// unscoped read the sync endpoint exists to avoid.
    /// </summary>
    [Fact]
    public void rejects_a_request_with_no_product_codes()
    {
        // Arrange
        var request = new SyncProductPricesRequest { ProductCodes = new List<string>() };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void bounds_a_single_product_code_at_fifty_characters(int codeLength, bool expectedValid)
    {
        // Arrange
        var request = new SyncProductPricesRequest
        {
            ProductCodes = new List<string> { new('A', codeLength) },
        };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().Be(expectedValid);
    }

    /// <summary>
    /// The ceiling is not a business limit — syncing with no filter applied must still pass —
    /// only a bound on what one request may ask the server to deserialize and hash.
    /// </summary>
    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void bounds_the_number_of_product_codes_in_one_request(int codeCount, bool expectedValid)
    {
        // Arrange
        var request = new SyncProductPricesRequest
        {
            ProductCodes = Enumerable.Range(1, codeCount).Select(i => $"P{i}").ToList(),
        };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void rejects_a_blank_product_code()
    {
        // Arrange
        var request = new SyncProductPricesRequest { ProductCodes = new List<string> { "A", "  " } };

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
