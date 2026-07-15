using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintLotLabelsRequestValidatorTests
{
    private readonly PrintLotLabelsRequestValidator _validator = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.TestValidate(
            new PrintLotLabelsRequest { LotNumber = "2926", Expiration = "07/29", Count = 5 });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789")] // > 8 chars
    public void Invalid_LotNumber_Fails(string lotNumber)
    {
        var result = _validator.TestValidate(
            new PrintLotLabelsRequest { LotNumber = lotNumber, Expiration = "07/29", Count = 1 });
        result.ShouldHaveValidationErrorFor(x => x.LotNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2029-07")]
    [InlineData("7/29")]
    [InlineData("July")]
    public void Invalid_Expiration_Fails(string expiration)
    {
        var result = _validator.TestValidate(
            new PrintLotLabelsRequest { LotNumber = "2926", Expiration = expiration, Count = 1 });
        result.ShouldHaveValidationErrorFor(x => x.Expiration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Invalid_Count_Fails(int count)
    {
        var result = _validator.TestValidate(
            new PrintLotLabelsRequest { LotNumber = "2926", Expiration = "07/29", Count = count });
        result.ShouldHaveValidationErrorFor(x => x.Count);
    }
}
