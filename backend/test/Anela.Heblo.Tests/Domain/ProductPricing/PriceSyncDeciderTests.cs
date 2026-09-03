using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.ProductPricing;

public class PriceSyncDeciderTests
{
    [Fact]
    public void returns_none_when_neither_heblo_nor_remote_moved_since_last_push()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.None);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_push_when_only_heblo_changed()
    {
        // Arrange
        const decimal heblo = 210.00m, lastPushed = 190.00m, remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Push);
        decision.PriceToPush.Should().Be(210.00m);
    }

    [Fact]
    public void returns_conflict_when_only_the_remote_changed()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 175.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(175.00m);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_conflict_when_both_heblo_and_the_remote_changed()
    {
        // Arrange
        const decimal heblo = 210.00m, lastPushed = 190.00m, remote = 175.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(175.00m);
        decision.PriceToPush.Should().BeNull();
    }

    [Fact]
    public void returns_seed_when_nothing_has_ever_been_pushed()
    {
        // Arrange
        const decimal heblo = 0m;
        decimal? lastPushed = null;
        const decimal remote = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Seed);
        decision.RemoteValue.Should().Be(190.00m);
    }

    [Fact]
    public void returns_missing_remote_when_the_product_is_absent_downstream()
    {
        // Arrange
        const decimal heblo = 190.00m;
        decimal? lastPushed = 190.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remotePriceWithVat: null);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.MissingRemote);
    }

    [Fact]
    public void missing_remote_wins_over_never_pushed()
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(190.00m, lastPushedPriceWithVat: null, remotePriceWithVat: null);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.MissingRemote);
    }

    [Theory]
    [InlineData(190.001, 190.004)]
    [InlineData(189.999, 190.001)]
    public void treats_differences_below_two_decimals_as_equal(decimal heblo, decimal remote)
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushedPriceWithVat: remote, remotePriceWithVat: remote);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.None);
    }

    [Fact]
    public void treats_a_one_haler_difference_as_a_real_change()
    {
        // Arrange & Act
        var decision = PriceSyncDecider.Decide(190.01m, lastPushedPriceWithVat: 190.00m, remotePriceWithVat: 190.00m);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Push);
        decision.PriceToPush.Should().Be(190.01m);
    }

    [Fact]
    public void a_within_tolerance_remote_difference_yields_none_when_tolerance_is_supplied()
    {
        // Arrange: 189.99 vs LastPushed 190.00 is exactly the Flexi with-VAT round-trip
        // rounding error, which a supplied tolerance must absorb as "no drift".
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 189.99m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote, remoteTolerance: 0.01m);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.None);
    }

    [Fact]
    public void the_same_within_tolerance_difference_still_conflicts_with_zero_tolerance()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 189.99m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote, remoteTolerance: 0m);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(189.99m);
    }

    [Fact]
    public void a_genuine_large_drift_still_conflicts_even_with_tolerance()
    {
        // Arrange
        const decimal heblo = 190.00m, lastPushed = 190.00m, remote = 175.00m;

        // Act
        var decision = PriceSyncDecider.Decide(heblo, lastPushed, remote, remoteTolerance: 0.01m);

        // Assert
        decision.Action.Should().Be(PriceSyncAction.Conflict);
        decision.RemoteValue.Should().Be(175.00m);
    }
}
