using Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters.Filters;

public class ReferenceTooLongFilterTests
{
    private readonly ReferenceTooLongFilter _filter = new();

    [Fact]
    public void CanHandle_WhenMessageContainsBothKeywords_ReturnsTrue()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Pole 'Číslo došlé' nesmí být delší než 40 znaků. [VYR51021]");

        _filter.CanHandle(ex).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WhenOnlyOneKeyword_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Pole 'Číslo došlé' has some error");

        _filter.CanHandle(ex).Should().BeFalse();
    }

    [Fact]
    public void Transform_ReturnsFixedMessage()
    {
        var ex = new InvalidOperationException(
            "Failed to create issued order: Pole 'Číslo došlé' nesmí být delší než 40 znaků. [VYR51021]");

        var result = _filter.Transform(ex);

        result.Should().Be("Číslo objednávky je příliš dlouhé pro systém Flexi (max. 40 znaků).");
    }
}
