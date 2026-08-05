using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxCodeRequiredException : ValidationException
{
    public TransportBoxCodeRequiredException()
        : base("Box code cannot be null or empty")
    {
    }
}

public class TransportBoxCodeFormatException : ValidationException
{
    public string EnteredCode { get; }

    public TransportBoxCodeFormatException(string enteredCode)
        : base("Box code must follow format: B + 3 digits (e.g., B001, B123)")
    {
        EnteredCode = enteredCode;
    }
}

public class TransportBoxEmptyException : ValidationException
{
    public string? BoxCode { get; }

    public TransportBoxEmptyException(string? boxCode)
        : base("Cannot transition to InTransit state: Box must contain at least one item")
    {
        BoxCode = boxCode;
    }
}

public class TransportBoxInvalidStateTransitionException : ValidationException
{
    public TransportBoxState CurrentState { get; }
    public IReadOnlyList<TransportBoxState> AllowedStates { get; }

    public TransportBoxInvalidStateTransitionException(
        string message, TransportBoxState currentState, IReadOnlyList<TransportBoxState> allowedStates)
        : base(message)
    {
        CurrentState = currentState;
        AllowedStates = allowedStates;
    }
}
