using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintMaterialContainerLabelsRequestValidatorTests
{
    private readonly PrintMaterialContainerLabelsRequestValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Valid_Count_PassesValidation(int count)
    {
        var result = _validator.TestValidate(new PrintMaterialContainerLabelsRequest { Count = count });
        result.ShouldNotHaveValidationErrorFor(x => x.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-5)]
    public void Invalid_Count_FailsValidation(int count)
    {
        var result = _validator.TestValidate(new PrintMaterialContainerLabelsRequest { Count = count });
        result.ShouldHaveValidationErrorFor(x => x.Count);
    }
}
