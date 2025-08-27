namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxTransition
{
    private readonly Action<TransportBox, DateTime, string>? _transitionCallback;

    public TransportBoxTransition(
        TransportBoxState newState,
        TransitionType  transitionType,
        Action<TransportBox, DateTime, string>? transitionCallback = null,
        Func<TransportBox, bool>? condition = null,
        bool systemOnly = false)
    {
        _transitionCallback = transitionCallback;
        NewState = newState;
        TransitionType = transitionType;
        Condition = condition;
        SystemOnly = systemOnly;
    }

    public TransportBoxState NewState { get; }
    public TransitionType TransitionType { get; }
    public Func<TransportBox, bool>? Condition { get; }
    public bool SystemOnly { get; }

    public Task<TransportBox> ChangeStateAsync(TransportBox box, DateTime actionDate, string username)
    {
        _transitionCallback?.Invoke(box, actionDate, username);
        return Task.FromResult(box);
    }
}