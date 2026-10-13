using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;
using Anela.Heblo.Application.Features.Pricing.Validators;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class UpdatePricingScenarioProductsRequestValidatorTests
{
    private readonly UpdatePricingScenarioProductsRequestValidator _validator = new();

    private static UpdatePricingScenarioProductsRequest Request() => new() { ScenarioId = Guid.NewGuid() };

    [Fact]
    public void A_request_that_changes_nothing_is_invalid()
    {
        _validator.Validate(Request()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_empty_scenario_id_is_invalid()
    {
        var request = new UpdatePricingScenarioProductsRequest { RemoveProductCodes = { "P1" } };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_new_name_is_invalid(string name)
    {
        var request = Request();
        request.Name = name;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_overlong_description_is_invalid()
    {
        var request = Request();
        request.Description = new string('x', 2001);

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_edit_without_a_product_code_is_invalid()
    {
        var request = Request();
        request.Edits.Add(new PricingEditDto { ProductCode = "", Field = PricingEditField.Price, Value = 100m });

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_blank_product_code_to_remove_is_invalid()
    {
        var request = Request();
        request.RemoveProductCodes.Add("");

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_request_with_an_edit_is_valid()
    {
        var request = Request();
        request.Edits.Add(new PricingEditDto { ProductCode = "P1", Field = PricingEditField.Price, Value = 100m });

        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_description_only_change_is_valid()
    {
        var request = Request();
        request.Description = "";

        _validator.Validate(request).IsValid.Should().BeTrue();
    }
}
