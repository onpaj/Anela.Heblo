using Anela.Heblo.Domain.Features.Catalog.Price;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class ProductPriceEshopTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static ProductPriceEshop Create(
        decimal? actionPrice,
        DateOnly? actionFrom = null,
        DateOnly? actionUntil = null,
        decimal regularPrice = 539.00m) =>
        ProductPriceEshop.FromPriceList(
            "BAL0001M", regularPrice, actionPrice, actionFrom, actionUntil, vatRate: 21m, today: Today);

    [Fact]
    public void uses_the_regular_price_when_there_is_no_action()
    {
        // Act
        var price = Create(actionPrice: null);

        // Assert
        price.IsInAction.Should().BeFalse();
        price.PriceWithVat.Should().Be(539.00m);
        price.RegularPriceWithVat.Should().Be(539.00m);
        price.PriceWithoutVat.Should().Be(445.45m);
    }

    [Fact]
    public void uses_an_open_ended_action_price()
    {
        // Act
        var price = Create(actionPrice: 490.00m);

        // Assert
        price.IsInAction.Should().BeTrue();
        price.PriceWithVat.Should().Be(490.00m);
        price.PriceWithoutVat.Should().Be(404.96m);
        price.RegularPriceWithVat.Should().Be(539.00m);
        price.ActionPriceWithVat.Should().Be(490.00m);
    }

    [Fact]
    public void uses_the_action_price_inside_its_window()
    {
        // Act
        var price = Create(490.00m, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31));

        // Assert
        price.IsInAction.Should().BeTrue();
        price.PriceWithVat.Should().Be(490.00m);
        price.ActionFrom.Should().Be(new DateOnly(2026, 9, 1));
        price.ActionUntil.Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void ignores_an_action_that_has_not_started_yet()
    {
        // Act
        var price = Create(490.00m, actionFrom: Today.AddDays(1));

        // Assert
        price.IsInAction.Should().BeFalse();
        price.PriceWithVat.Should().Be(539.00m);
        price.ActionPriceWithVat.Should().Be(490.00m);
    }

    [Fact]
    public void ignores_an_action_that_has_already_ended()
    {
        // Act — BAL0007M shape: an expired Christmas action left in the price list
        var price = Create(620.00m, new DateOnly(2025, 11, 18), new DateOnly(2025, 12, 23), regularPrice: 669.00m);

        // Assert
        price.IsInAction.Should().BeFalse();
        price.PriceWithVat.Should().Be(669.00m);
    }

    [Fact]
    public void treats_the_start_day_as_part_of_the_action()
    {
        // Act
        var price = Create(490.00m, actionFrom: Today);

        // Assert
        price.IsInAction.Should().BeTrue();
    }

    [Fact]
    public void treats_the_end_day_as_part_of_the_action()
    {
        // Act
        var price = Create(490.00m, actionUntil: Today);

        // Assert
        price.IsInAction.Should().BeTrue();
    }

    [Fact]
    public void ignores_an_action_whose_end_day_was_yesterday()
    {
        // Act
        var price = Create(490.00m, actionUntil: Today.AddDays(-1));

        // Assert
        price.IsInAction.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ignores_a_non_positive_action_price(int actionPrice)
    {
        // Act
        var price = Create(actionPrice);

        // Assert
        price.IsInAction.Should().BeFalse();
        price.PriceWithVat.Should().Be(539.00m);
    }
}
