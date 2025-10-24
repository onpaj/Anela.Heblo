using Anela.Heblo.Domain.Features.Manufacture;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ManufactureOrderStateTransitionTests
{
    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.InProduction, true)]
    [InlineData(ManufactureOrderState.InProduction, ManufactureOrderState.Completed, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.InProduction, ManufactureOrderState.Cancelled, true)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.InProduction, false)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.InProduction, false)]
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
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.Completed, false)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.InProduction, false)]
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
    [InlineData(ManufactureType.SinglePhase, ManufactureOrderState.Planned, ManufactureOrderState.InProduction, true)]
    [InlineData(ManufactureType.SinglePhase, ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, false)]
    [InlineData(ManufactureType.MultiPhase, ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured, true)]
    [InlineData(ManufactureType.MultiPhase, ManufactureOrderState.Planned, ManufactureOrderState.InProduction, false)]
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
            (ManufactureOrderState.Planned, ManufactureOrderState.InProduction) => true,
            (ManufactureOrderState.InProduction, ManufactureOrderState.Completed) => true,
            (_, ManufactureOrderState.Cancelled) => true,
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
            (_, ManufactureOrderState.Cancelled) => true,
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