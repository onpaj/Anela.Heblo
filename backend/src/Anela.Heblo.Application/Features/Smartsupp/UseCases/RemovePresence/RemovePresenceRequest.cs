using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RemovePresence;

public sealed class RemovePresenceRequest : IRequest<RemovePresenceResponse>
{
    public string ConversationId { get; set; } = null!;
}
