using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ImportNotAllowedFilterTests
{
    private const string FriendlyMessage =
        "FlexiBee odmítl vytvoření skladového dokladu (import není povolen). Doklad nebyl vytvořen. "
        + "Zkuste akci zopakovat; pokud problém přetrvává, kontaktujte správce systému.";

    private readonly ImportNotAllowedFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsWinstromImportNotAllowedCode_ReturnsTrue()
    {
        var ex = new FlexiManufactureException(
            FlexiManufactureOperationKind.ConsumptionMovement,
            "Failed to create consumption stock movement for warehouse 5",
            warehouseId: 5,
            rawFlexiError: "{\"winstrom\":{\"success\":\"false\",\"message@messageCode\":\"importNotAllowed\"}}");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageContainsCzechImportNotAllowedText_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create consumption stock movement for warehouse 5: Import není povolen.");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenMessageIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFriendlyMessage()
    {
        var ex = new FlexiManufactureException(
            FlexiManufactureOperationKind.ConsumptionMovement,
            "Failed to create consumption stock movement for warehouse 5",
            warehouseId: 5,
            rawFlexiError: "{\"winstrom\":{\"success\":\"false\",\"message@messageCode\":\"importNotAllowed\",\"message\":\"Import není povolen.\"}}");

        var result = _filter.Transform(ex);

        result.Should().Be(FriendlyMessage);
    }
}
