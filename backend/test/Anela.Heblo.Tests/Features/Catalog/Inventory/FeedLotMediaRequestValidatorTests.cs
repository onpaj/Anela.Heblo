using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class FeedLotMediaRequestValidatorTests
{
    private readonly FeedLotMediaRequestValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(200)]
    public void Valid_Dots_Pass(int dots)
    {
        var result = _validator.TestValidate(new FeedLotMediaRequest { Dots = dots });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void Invalid_Dots_Fail(int dots)
    {
        var result = _validator.TestValidate(new FeedLotMediaRequest { Dots = dots });
        result.ShouldHaveValidationErrorFor(x => x.Dots);
    }
}
