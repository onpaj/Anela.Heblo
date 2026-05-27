using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class CreateOrderShipmentRequestValidatorTests
{
    private readonly CreateOrderShipmentRequestValidator _validator = new();

    [Fact]
    public async Task Validate_WithValidOrderCode_IsValid()
    {
        var result = await _validator.ValidateAsync(
            new CreateOrderShipmentRequest { OrderCode = "0001234" });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Validate_WithEmptyOrNullOrderCode_IsInvalid(string? orderCode)
    {
        var result = await _validator.ValidateAsync(
            new CreateOrderShipmentRequest { OrderCode = orderCode! });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrderCode");
    }
}
