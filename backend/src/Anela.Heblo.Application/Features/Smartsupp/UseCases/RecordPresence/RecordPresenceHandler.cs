using Anela.Heblo.Application.Features.Smartsupp.Presence;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RecordPresence;

public sealed class RecordPresenceHandler : IRequestHandler<RecordPresenceRequest, RecordPresenceResponse>
{
    private readonly ISmartsuppPresenceService _presenceService;

    public RecordPresenceHandler(ISmartsuppPresenceService presenceService)
        => _presenceService = presenceService;

    public async Task<RecordPresenceResponse> Handle(RecordPresenceRequest request, CancellationToken cancellationToken)
    {
        await _presenceService.RecordCurrentOperatorAsync(request.ConversationId, cancellationToken);
        return new RecordPresenceResponse();
    }
}
