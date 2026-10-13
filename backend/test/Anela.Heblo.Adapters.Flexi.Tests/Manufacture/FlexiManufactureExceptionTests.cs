using Anela.Heblo.Adapters.Flexi.Manufacture;
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureExceptionTests
{
    [Theory]
    [InlineData(FlexiManufactureOperationKind.StockValidation, true)]
    [InlineData(FlexiManufactureOperationKind.Allocation, true)]
    [InlineData(FlexiManufactureOperationKind.ConsumptionMovement, false)]
    [InlineData(FlexiManufactureOperationKind.ProductionMovement, false)]
    [InlineData(FlexiManufactureOperationKind.TemplateFetch, false)]
    public void IsStockShortage_IsTrueOnlyForPreSubmitStockFailures(FlexiManufactureOperationKind kind, bool expected)
    {
        // Arrange
        var exception = new FlexiManufactureException(kind, "failure");

        // Act
        var shortage = exception as IManufactureStockShortage;

        // Assert
        shortage.Should().NotBeNull();
        shortage!.IsStockShortage.Should().Be(expected);
    }
}
