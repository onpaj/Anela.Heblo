using Anela.Heblo.Application.Features.Smartsupp.Presence;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RemovePresence;

public sealed class RemovePresenceHandler : IRequestHandler<RemovePresenceRequest, RemovePresenceResponse>
{
    private readonly ISmartsuppPresenceService _presenceService;

    public RemovePresenceHandler(ISmartsuppPresenceService presenceService)
        => _presenceService = presenceService;

    public async Task<RemovePresenceResponse> Handle(RemovePresenceRequest request, CancellationToken cancellationToken)
    {
        await _presenceService.RemoveCurrentOperatorAsync(request.ConversationId, cancellationToken);
        return new RemovePresenceResponse();
    }
}
