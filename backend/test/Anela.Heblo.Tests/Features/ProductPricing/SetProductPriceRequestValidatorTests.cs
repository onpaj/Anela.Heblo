using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

/// <summary>
/// The 0.01 floor is the only thing between a mistyped or cleared price and a PATCH that
/// Shoptet treats as a genuine free price on the live shop (from 2026-09-14). Both adapters
/// serialize with ToString("F2"), so anything under 0.01 reaches the wire as "0.00".
///
/// The 1 000 000 ceiling is the other end of the same guard: the UI's large-change
/// confirmation cannot protect a caller that is not the UI.
/// </summary>
public class SetProductPriceRequestValidatorTests
{
    private readonly SetProductPriceRequestValidator _validator = new();

    private static SetProductPriceRequest Request(string code = "MAS001180", decimal price = 190.00m) =>
        new() { ProductCode = code, PriceWithVat = price };

    [Theory]
    [InlineData(1_000_000.01)]
    [InlineData(9_999_999.99)]
    public void rejects_a_price_above_the_sanity_ceiling(decimal price)
    {
        // Act
        var result = _validator.Validate(Request(price: price));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetProductPriceRequest.PriceWithVat));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(190.00)]
    [InlineData(1_000_000)]
    public void accepts_a_price_inside_the_bounds(decimal price)
    {
        // Act
        var result = _validator.Validate(Request(price: price));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-190.00)]
    [InlineData(0.004)]
    [InlineData(0.009)]
    public void rejects_a_price_that_would_reach_the_wire_as_zero_or_negative(decimal price)
    {
        // Act
        var result = _validator.Validate(Request(price: price));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetProductPriceRequest.PriceWithVat));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(190.00)]
    [InlineData(99999.99)]
    public void accepts_a_price_at_or_above_the_floor(decimal price)
    {
        // Act & Assert
        _validator.Validate(Request(price: price)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void rejects_an_empty_product_code(string code)
    {
        // Act
        var result = _validator.Validate(Request(code: code));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetProductPriceRequest.ProductCode));
    }

    [Fact]
    public void rejects_a_product_code_longer_than_the_column()
    {
        // Act
        var result = _validator.Validate(Request(code: new string('A', 51)));

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetProductPriceRequest.ProductCode));
    }

    [Fact]
    public void accepts_a_well_formed_request()
    {
        // Act & Assert
        _validator.Validate(Request()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void the_validator_is_a_fluent_validation_validator_for_this_request()
    {
        // Guards the type the module registration binds to.
        _validator.Should().BeAssignableTo<IValidator<SetProductPriceRequest>>();
    }
}
