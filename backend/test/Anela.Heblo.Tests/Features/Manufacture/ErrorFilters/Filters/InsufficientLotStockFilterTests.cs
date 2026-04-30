using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class InsufficientLotStockFilterTests
{
    private const string RealFlexiError =
        "Na skladě není dostatek zboží pro vyskladnění požadované šarže nebo expirace. " +
        "Požadované šarže 171224A7 s expirací 17.12.2027 máte na skladě jen 6.597599 G. [DoklSklad -1]";

    private readonly InsufficientLotStockFilter _filter = new();

    private sealed class EnrichedManufactureException(string message, IReadOnlyList<FailedConsumptionItem>? items = null)
        : Exception(message), IHasFailedConsumptionItems
    {
        public IReadOnlyList<FailedConsumptionItem> FailedItems { get; } = items ?? [];
    }

    [Fact]
    public void CanHandle_WhenExceptionIsUnrelated_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some other error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenExceptionDoesNotImplementInterface_ReturnsFalse()
    {
        var ex = new InvalidOperationException(RealFlexiError);

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WhenEnrichedExceptionContainsShortageMessage_ReturnsTrue()
    {
        var ex = new EnrichedManufactureException(RealFlexiError);

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void Transform_WhenLotMatchesFailedItem_IncludesMaterialNameAndCode()
    {
        var items = new List<FailedConsumptionItem>
        {
            new("MAT-LAVENDER", "Levandulový olej", "171224A7", new DateOnly(2027, 12, 17), 10.0)
        };
        var ex = new EnrichedManufactureException(RealFlexiError, items);

        var result = _filter.Transform(ex);

        result.Should().Contain("Levandulový olej");
        result.Should().Contain("MAT-LAVENDER");
        result.Should().Contain("171224A7");
        result.Should().Contain("17.12.2027");
        result.Should().Contain("6.597599 G");
    }

    [Fact]
    public void Transform_WhenNoFailedItems_FallsBackToLotOnlyMessage()
    {
        var ex = new EnrichedManufactureException(RealFlexiError, []);

        var result = _filter.Transform(ex);

        result.Should().Contain("171224A7");
        result.Should().Contain("nepodařilo dohledat");
        result.Should().NotContain("MAT-");
    }

    [Fact]
    public void Transform_WhenLotNotInFailedItems_FallsBackToLotOnlyMessage()
    {
        var items = new List<FailedConsumptionItem>
        {
            new("MAT-OTHER", "Jiný materiál", "999999X1", new DateOnly(2026, 6, 1), 5.0)
        };
        var ex = new EnrichedManufactureException(RealFlexiError, items);

        var result = _filter.Transform(ex);

        result.Should().Contain("171224A7");
        result.Should().Contain("nepodařilo dohledat");
    }

    [Fact]
    public void Transform_WhenMessageFormatUnrecognized_FallsBackToLotOnlyMessage()
    {
        var items = new List<FailedConsumptionItem>
        {
            new("MAT-LAVENDER", "Levandulový olej", "171224A7", new DateOnly(2027, 12, 17), 10.0)
        };
        // Message contains the CanHandle signal but uses a format where the lot cannot be parsed.
        var ex = new EnrichedManufactureException(
            "Na skladě není dostatek zboží pro vyskladnění požadované šarže nebo expirace. Unexpected format without lot.",
            items);

        var result = _filter.Transform(ex);

        result.Should().Contain("nepodařilo dohledat");
        result.Should().NotContain("Levandulový olej");
    }

    [Fact]
    public void Transform_WhenTwoItemsShareLot_DisambiguatesByExpiration()
    {
        var items = new List<FailedConsumptionItem>
        {
            new("MAT-A", "Materiál A", "171224A7", new DateOnly(2025, 1, 1), 5.0),
            new("MAT-B", "Materiál B", "171224A7", new DateOnly(2027, 12, 17), 5.0)
        };
        var ex = new EnrichedManufactureException(RealFlexiError, items);

        var result = _filter.Transform(ex);

        result.Should().Contain("MAT-B");
        result.Should().Contain("Materiál B");
        result.Should().NotContain("MAT-A");
    }
}
