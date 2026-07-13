using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RecordPresence;

public sealed class RecordPresenceRequest : IRequest<RecordPresenceResponse>
{
    public string ConversationId { get; set; } = null!;
}
