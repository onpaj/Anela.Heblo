namespace Anela.Heblo.Application.Domain.Logistics.Transport;

public class TransportBoxAction
{
    private readonly Action<TransportBox, DateTime, string>? _transitionCallback;

    public TransportBoxAction(
        TransportBoxState newState, 
        Action<TransportBox, DateTime, string>? transitionCallback = null,
        Func<TransportBox, bool>? condition = null)
    {
        _transitionCallback = transitionCallback;
        NewState = newState;
        Condition = condition;
    }

    public TransportBoxState NewState { get; }
    public Func<TransportBox, bool>? Condition { get; }

    public Task<TransportBox> ChangeStateAsync(TransportBox box, DateTime actionDate, string username)
    {
        _transitionCallback?.Invoke(box, actionDate, username);
        return Task.FromResult(box);
    }
}

