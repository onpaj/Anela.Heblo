using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxStateNode
{
    public TransportBoxAction? NextState { get; set; }
    public TransportBoxAction? PreviousState { get; set; }

    public TransportBoxAction GetTransition(TransportBoxState targetState)
    {
        if (targetState == NextState?.NewState)
            return NextState;
        if (targetState == PreviousState?.NewState)
            return PreviousState;

        throw new ValidationException($"Unable to change state to {targetState}");
    }
}