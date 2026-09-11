using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

// No location required for Quarantine — ToQuarantine() clears Location = null.
// Kept as an explicit, registered side effect (rather than omitted from dispatch)
// so future Quarantine-entry behavior has one obvious place to be added, and so
// dispatch-uniqueness tests can assert exactly one strategy handles this pair.
public class OpenToQuarantineSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Quarantine;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
