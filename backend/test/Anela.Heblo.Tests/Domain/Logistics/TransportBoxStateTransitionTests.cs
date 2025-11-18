using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class TransportBoxStateTransitionTests
{
    private const string TestUser = "TestUser";
    private readonly DateTime _testDate = DateTime.UtcNow;

    [Theory]
    [InlineData(TransportBoxState.New, TransportBoxState.Opened)]
    [InlineData(TransportBoxState.New, TransportBoxState.Closed)]
    public void ValidTransitionsFromNew_ShouldSucceed(TransportBoxState fromState, TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(fromState);
        if (toState == TransportBoxState.Opened)
        {
            box.Open("B001", _testDate, TestUser);
        }

        // Act & Assert
        if (toState == TransportBoxState.Closed)
        {
            var act = () => box.Close(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Closed);
        }
    }

    [Theory]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.New)]
    public void ValidTransitionsFromOpened_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);

        // Act & Assert
        if (toState == TransportBoxState.InTransit)
        {
            var act = () => box.ToTransit(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.InTransit);
        }
        else if (toState == TransportBoxState.Reserve)
        {
            var act = () => box.ToReserve(_testDate, TestUser, TransportBoxLocation.Kumbal);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Reserve);
        }
        else if (toState == TransportBoxState.New)
        {
            var act = () => box.Reset(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.New);
            box.Code.Should().BeNull();
            box.Items.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Opened)]
    public void ValidTransitionsFromInTransit_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToTransit(_testDate, TestUser);

        // Act & Assert
        if (toState == TransportBoxState.Received)
        {
            var act = () => box.Receive(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Received);
        }
        else if (toState == TransportBoxState.Opened)
        {
            // Revert transition back to Opened - using RevertToOpened method
            var act = () => box.RevertToOpened(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Opened);
        }
    }

    [Theory]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Opened)]
    public void ValidTransitionsFromReserve_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToReserve(_testDate, TestUser, TransportBoxLocation.Kumbal);

        // Act & Assert
        if (toState == TransportBoxState.Received)
        {
            var act = () => box.Receive(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Received);
        }
        else if (toState == TransportBoxState.Opened)
        {
            // Revert transition back to Opened - using RevertToOpened method
            var act = () => box.RevertToOpened(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Opened);
        }
    }

    [Theory]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Closed)]
    public void ValidTransitionsFromReceived_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToTransit(_testDate, TestUser);
        box.Receive(_testDate, TestUser);

        // Act & Assert
        if (toState == TransportBoxState.Stocked)
        {
            var act = () => box.ToPick(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Stocked);
        }
        else if (toState == TransportBoxState.Closed)
        {
            var act = () => box.Close(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Closed);
        }
    }

    [Theory]
    [InlineData(TransportBoxState.Closed)]
    public void ValidTransitionsFromStocked_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Stocked);
        SetBoxCode(box, "B001");

        // Act & Assert
        if (toState == TransportBoxState.Closed)
        {
            var act = () => box.Close(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Closed);
        }
    }

    // Note: According to the specification, Closed and Error states have no valid outbound transitions except Error

    [Theory]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromNew_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Closed)]
    public void InvalidTransitionsFromOpened_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Closed)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromInTransit_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToTransit(_testDate, TestUser);

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Closed)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromReserve_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToReserve(_testDate, TestUser, TransportBoxLocation.Kumbal);

        // Act & Assert  
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Opened)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromReceived_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product", 1.0, _testDate, TestUser);
        box.ToTransit(_testDate, TestUser);
        box.Receive(_testDate, TestUser);

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Opened)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromStocked_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Stocked);
        SetBoxCode(box, "B001");

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Opened)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Reserve)]
    public void InvalidTransitionsFromClosed_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Closed);
        SetBoxCode(box, "B001");

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Reserve)]
    public void ValidTransitionsFromError_ShouldSucceed(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Error);
        SetBoxCode(box, "B001");

        // Act & Assert
        if (toState == TransportBoxState.Stocked)
        {
            var act = () => box.ToPick(_testDate, TestUser);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Stocked);
        }
        else if (toState == TransportBoxState.Reserve)
        {
            var act = () => box.ToReserve(_testDate, TestUser, TransportBoxLocation.Kumbal);
            act.Should().NotThrow();
            box.State.Should().Be(TransportBoxState.Reserve);
        }
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Opened)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Closed)]
    public void InvalidTransitionsFromError_ShouldThrow(TransportBoxState toState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Error);
        SetBoxCode(box, "B001");

        // Act & Assert
        AssertInvalidTransition(box, toState);
    }

    [Theory]
    [InlineData(TransportBoxState.New)]
    [InlineData(TransportBoxState.Opened)]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Received)]
    [InlineData(TransportBoxState.Stocked)]
    [InlineData(TransportBoxState.Closed)]
    [InlineData(TransportBoxState.Reserve)]
    public void TransitionToError_FromAnyState_ShouldSucceed(TransportBoxState fromState)
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(fromState);
        if (fromState != TransportBoxState.New)
        {
            SetBoxCode(box, "B001");
        }

        // Act
        var act = () => box.Error(_testDate, TestUser, "Test error message");

        // Assert
        act.Should().NotThrow();
        box.State.Should().Be(TransportBoxState.Error);
    }

    [Fact]
    public void ResetToNew_ShouldClearCodeAndItems()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);
        box.AddItem("PRODUCT001", "Test Product 1", 2.0, _testDate, TestUser);
        box.AddItem("PRODUCT002", "Test Product 2", 3.0, _testDate, TestUser);

        // Verify box has code and items before reset
        box.Code.Should().Be("B001");
        box.Items.Should().HaveCount(2);
        box.State.Should().Be(TransportBoxState.Opened);

        // Act
        box.Reset(_testDate, TestUser);

        // Assert
        box.State.Should().Be(TransportBoxState.New);
        box.Code.Should().BeNull();
        box.Items.Should().BeEmpty();
    }

    [Fact]
    public void TransitWithoutItems_ShouldThrow()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);
        box.Open("B001", _testDate, TestUser);

        // Act
        var act = () => box.ToTransit(_testDate, TestUser);

        // Assert
        act.Should().Throw<ValidationException>()
           .WithMessage("Cannot transition to InTransit state: Box must contain at least one item");
    }

    [Fact]
    public void AssignBoxNumber_InvalidFormat_ShouldThrow()
    {
        // Arrange
        var box = CreateBoxInState(TransportBoxState.New);

        // Act & Assert
        var act1 = () => box.Open("B12", _testDate, TestUser); // Too short
        act1.Should().Throw<ValidationException>()
            .WithMessage("Box code must follow format: B + 3 digits (e.g., B001, B123)");

        var act2 = () => box.Open("C001", _testDate, TestUser); // Wrong prefix
        act2.Should().Throw<ValidationException>()
            .WithMessage("Box code must follow format: B + 3 digits (e.g., B001, B123)");

        var act3 = () => box.Open("B1A1", _testDate, TestUser); // Non-numeric
        act3.Should().Throw<ValidationException>()
            .WithMessage("Box code must follow format: B + 3 digits (e.g., B001, B123)");
    }

    [Fact]
    public void AssignBoxNumber_NotInNewState_ShouldThrow()
    {
        // Arrange
        var box = CreateBoxInStateUsingReflection(TransportBoxState.Opened);

        // Act
        var act = () => box.Open("B001", _testDate, TestUser);

        // Assert
        act.Should().Throw<ValidationException>()
           .WithMessage("Box number can only be assigned to boxes in 'New' state. Current state: Opened");
    }

    private TransportBox CreateBoxInState(TransportBoxState state)
    {
        // Always creates box in New state - this is the natural initial state
        // State transitions are then performed through proper business methods
        return new TransportBox();
    }

    private TransportBox CreateBoxInStateUsingReflection(TransportBoxState state)
    {
        var box = new TransportBox();
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);
        return box;
    }

    private void SetBoxCode(TransportBox box, string code)
    {
        var codeField = typeof(TransportBox).GetField("<Code>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        codeField?.SetValue(box, code);
    }

    private void AssertInvalidTransition(TransportBox box, TransportBoxState toState)
    {
        Action act = toState switch
        {
            TransportBoxState.Opened => () =>
            {
                // For invalid transitions, we try both methods that could lead to Opened
                try { box.Open("B002", _testDate, TestUser); }
                catch { box.RevertToOpened(_testDate, TestUser); }
            }
            ,
            TransportBoxState.InTransit => () => box.ToTransit(_testDate, TestUser),
            TransportBoxState.Received => () => box.Receive(_testDate, TestUser),
            TransportBoxState.Stocked => () => box.ToPick(_testDate, TestUser),
            TransportBoxState.Closed => () => box.Close(_testDate, TestUser),
            TransportBoxState.Reserve => () => box.ToReserve(_testDate, TestUser, TransportBoxLocation.Kumbal),
            TransportBoxState.New => () => box.Reset(_testDate, TestUser),
            _ => () => { }
        };

        act.Should().Throw<ValidationException>();
    }
}