using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

public class CloseConversationRequest : IRequest<CloseConversationResponse>
{
    public string ConversationId { get; set; } = null!;
}
