using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxStateNode
{
    private readonly List<TransportBoxAction> _allowedTransitions = new();

    public TransportBoxAction? NextState { get; set; }
    public TransportBoxAction? PreviousState { get; set; }

    public void AddTransition(TransportBoxAction action)
    {
        _allowedTransitions.Add(action);
    }

    public TransportBoxAction GetTransition(TransportBoxState targetState)
    {
        // Check new multiple transitions first
        var transition = _allowedTransitions.FirstOrDefault(t => t.NewState == targetState);
        if (transition != null)
            return transition;

        // Fall back to legacy NextState/PreviousState for backward compatibility
        if (targetState == NextState?.NewState)
            return NextState;
        if (targetState == PreviousState?.NewState)
            return PreviousState;

        throw new ValidationException($"Unable to change state to {targetState}");
    }

    public IEnumerable<TransportBoxState> GetAllowedTransitions()
    {
        var transitions = _allowedTransitions.Select(t => t.NewState).ToList();
        
        if (NextState != null)
            transitions.Add(NextState.NewState);
        if (PreviousState != null)
            transitions.Add(PreviousState.NewState);

        return transitions.Distinct();
    }
}