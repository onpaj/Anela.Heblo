using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class PrintExpeditionOrderRequestValidatorTests
{
    private readonly PrintExpeditionOrderRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyOrderCode_Fails()
    {
        var result = _validator.Validate(new PrintExpeditionOrderRequest { OrderCode = "" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NonEmptyOrderCode_Passes()
    {
        var result = _validator.Validate(new PrintExpeditionOrderRequest { OrderCode = "0001234" });
        result.IsValid.Should().BeTrue();
    }
}
