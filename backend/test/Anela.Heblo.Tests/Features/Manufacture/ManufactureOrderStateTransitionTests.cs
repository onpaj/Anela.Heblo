using Anela.Heblo.Domain.Features.Manufacture;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderStateTransitionTests
{
    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, false)] // SinglePhase: Cannot go to SemiProductManufactured
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed, false)] // SinglePhase: Never in SemiProductManufactured state
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Cancelled, false)] // SinglePhase: Never in SemiProductManufactured state
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Completed, true)] // Single-phase: Planned → Completed directly
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Planned, false)]
    public void ValidateSinglePhaseTransition_ShouldReturnExpectedResult(
        ManufactureOrderState currentState,
        ManufactureOrderState targetState,
        bool expectedValid)
    {
        // Act
        var result = ValidateSinglePhaseTransition(currentState, targetState);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Completed, false)] // Multi-phase cannot go directly from Planned to Completed
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Planned, false)]
    public void ValidateMultiPhaseTransition_ShouldReturnExpectedResult(
        ManufactureOrderState currentState,
        ManufactureOrderState targetState,
        bool expectedValid)
    {
        // Act
        var result = ValidateMultiPhaseTransition(currentState, targetState);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    [Theory]
    [InlineData(ManufactureType.SinglePhase, ManufactureOrderState.Planned, ManufactureOrderState.Completed, true)]
    [InlineData(ManufactureType.SinglePhase, ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureType.MultiPhase, ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, true)]
    [InlineData(ManufactureType.MultiPhase, ManufactureOrderState.Planned, ManufactureOrderState.Completed, false)]
    public void IsValidStateTransition_ShouldRespectManufactureType(
        ManufactureType manufactureType,
        ManufactureOrderState currentState,
        ManufactureOrderState targetState,
        bool expectedValid)
    {
        // Act
        var result = IsValidStateTransition(currentState, targetState, manufactureType);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    // Helper methods that mirror the private methods in ManufactureOrderApplicationService
    private bool ValidateSinglePhaseTransition(ManufactureOrderState current, ManufactureOrderState target)
    {
        return (current, target) switch
        {
            (ManufactureOrderState.Draft, ManufactureOrderState.Planned) => true,
            (ManufactureOrderState.Planned, ManufactureOrderState.Completed) => true, // Single-phase: Planned → Completed directly
            // SinglePhase can be cancelled from Draft and Planned states (not from final states)
            (ManufactureOrderState.Draft, ManufactureOrderState.Cancelled) => true,
            (ManufactureOrderState.Planned, ManufactureOrderState.Cancelled) => true,
            _ => false
        };
    }

    private bool ValidateMultiPhaseTransition(ManufactureOrderState current, ManufactureOrderState target)
    {
        return (current, target) switch
        {
            (ManufactureOrderState.Draft, ManufactureOrderState.Planned) => true,
            (ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured) => true,
            (ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed) => true,
            // MultiPhase can be cancelled from Draft, Planned, and SemiProductManufactured states
            (ManufactureOrderState.Draft, ManufactureOrderState.Cancelled) => true,
            (ManufactureOrderState.Planned, ManufactureOrderState.Cancelled) => true,
            (ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Cancelled) => true,
            _ => false
        };
    }

    private bool IsValidStateTransition(ManufactureOrderState currentState, ManufactureOrderState newState, ManufactureType manufactureType)
    {
        return manufactureType switch
        {
            ManufactureType.SinglePhase => ValidateSinglePhaseTransition(currentState, newState),
            ManufactureType.MultiPhase => ValidateMultiPhaseTransition(currentState, newState),
            _ => false
        };
    }
}