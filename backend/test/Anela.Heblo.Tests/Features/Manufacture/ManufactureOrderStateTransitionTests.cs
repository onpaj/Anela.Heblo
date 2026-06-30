using System;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Manufacture;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderStateTransitionTests
{
    [Theory]
    // Draft row
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Draft, false)]
    // Planned row
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Draft, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Completed, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Planned, false)]
    // SemiProductManufactured row
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Draft, false)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.SemiProductManufactured, false)]
    // Completed row
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.SemiProductManufactured, true)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Draft, false)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Completed, false)]
    // Cancelled row (terminal)
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Draft, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Planned, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Cancelled, false)]
    public void CanTransitionTo_ReturnsExpected(
        ManufactureOrderState from,
        ManufactureOrderState to,
        bool expected)
    {
        // Arrange
        var order = OrderInState(from);

        // Act
        var result = order.CanTransitionTo(to);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ChangeState_OnIllegalTransition_ThrowsAndLeavesEntityUnchanged()
    {
        // Arrange
        var changedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var order = new ManufactureOrder();
        order.InitializeState(ManufactureOrderState.Cancelled, changedAt, "seed-user");

        // Act + Assert
        Assert.Throws<ValidationException>(() =>
            order.ChangeState(ManufactureOrderState.Planned, DateTime.UtcNow, "user"));

        Assert.Equal(ManufactureOrderState.Cancelled, order.State);
        Assert.Equal(changedAt, order.StateChangedAt);
        Assert.Equal("seed-user", order.StateChangedByUser);
    }

    [Fact]
    public void ChangeState_OnLegalTransition_UpdatesAllThreeFields()
    {
        // Arrange
        var order = OrderInState(ManufactureOrderState.Draft);
        var someUtc = new DateTime(2026, 6, 18, 10, 30, 0, DateTimeKind.Utc);

        // Act
        order.ChangeState(ManufactureOrderState.Planned, someUtc, "alice");

        // Assert
        Assert.Equal(ManufactureOrderState.Planned, order.State);
        Assert.Equal(someUtc, order.StateChangedAt);
        Assert.Equal("alice", order.StateChangedByUser);
    }

    private static ManufactureOrder OrderInState(ManufactureOrderState state)
    {
        var order = new ManufactureOrder();
        order.InitializeState(state, DateTime.UtcNow, "test");
        return order;
    }
}
