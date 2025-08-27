using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxStateNode
{
    private readonly List<TransportBoxTransition> _allowedTransitions = new();

    public void AddTransition(TransportBoxTransition action)
    {
        _allowedTransitions.Add(action);
    }
    
    public TransportBoxTransition GetTransition(TransportBoxState targetState)
    {
        // Check new multiple transitions first
        var transition = _allowedTransitions.FirstOrDefault(t => t.NewState == targetState);
        if (transition != null)
            return transition;

        throw new ValidationException($"Unable to change state to {targetState}");
    }

    public IEnumerable<TransportBoxState> GetAllowedTransitions()
    {
        var transitions = _allowedTransitions.Select(t => t.NewState).ToList();
        return transitions.Distinct();
    }

    public IEnumerable<TransportBoxTransition> GetAllTransitions()
    {
        return _allowedTransitions.AsReadOnly();
    }
}

