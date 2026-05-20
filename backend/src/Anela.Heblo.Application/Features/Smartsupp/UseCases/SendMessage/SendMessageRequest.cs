using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageRequest : IRequest<SendMessageResponse>
{
    public string ConversationId { get; set; } = null!;
    public string Content { get; set; } = null!;
}
