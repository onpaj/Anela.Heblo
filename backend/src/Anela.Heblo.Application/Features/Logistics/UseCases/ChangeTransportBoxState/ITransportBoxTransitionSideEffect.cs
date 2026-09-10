using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

/// <summary>
/// One state-transition's side effect, dispatched by ChangeTransportBoxStateHandler.
/// Return null to let the transition continue; return a populated response to
/// short-circuit Handle() with a failure result — identical contract to the
/// private methods this interface replaces.
/// </summary>
public interface ITransportBoxTransitionSideEffect
{
    bool Supports(TransportBoxState from, TransportBoxState to);

    Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box,
        ChangeTransportBoxStateRequest request,
        CancellationToken cancellationToken);
}
