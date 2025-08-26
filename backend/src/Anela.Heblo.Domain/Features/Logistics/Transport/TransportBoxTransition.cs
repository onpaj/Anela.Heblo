namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxAction
{
    private readonly Action<TransportBox, string?, DateTime, string>? _transitionCallback;

    public TransportBoxAction(
        TransportBoxState newState,
        Action<TransportBox, string?, DateTime, string>? transitionCallback = null,
        Func<TransportBox, bool>? condition = null)
    {
        _transitionCallback = transitionCallback;
        NewState = newState;
        Condition = condition;
    }

    public TransportBoxState NewState { get; }
    public Func<TransportBox, bool>? Condition { get; }

    public Task<TransportBox> ChangeStateAsync(TransportBox box, string? newBoxNumber, DateTime actionDate, string username)
    {
        _transitionCallback?.Invoke(box, newBoxNumber, actionDate, username);
        return Task.FromResult(box);
    }
}

