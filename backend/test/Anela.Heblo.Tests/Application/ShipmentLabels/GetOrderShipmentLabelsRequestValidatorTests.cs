using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation.TestHelper;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class GetOrderShipmentLabelsRequestValidatorTests
{
    private readonly GetOrderShipmentLabelsRequestValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceOrderCode_ReturnsValidationError(string orderCode)
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = orderCode };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderCode);
    }

    [Fact]
    public void Validate_NullOrderCode_ReturnsValidationError()
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = null! };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.OrderCode);
    }

    [Fact]
    public void Validate_ValidOrderCode_ReturnsNoErrors()
    {
        // Arrange
        var request = new GetOrderShipmentLabelsRequest { OrderCode = "0001234" };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
