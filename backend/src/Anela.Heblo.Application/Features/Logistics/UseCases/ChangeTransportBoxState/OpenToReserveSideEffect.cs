using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class OpenToReserveSideEffect : ITransportBoxTransitionSideEffect
{
    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.Opened && to == TransportBoxState.Reserve;

    public Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Location))
        {
            return Task.FromResult<ChangeTransportBoxStateResponse?>(new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "Location" } }
            });
        }

        return Task.FromResult<ChangeTransportBoxStateResponse?>(null);
    }
}
