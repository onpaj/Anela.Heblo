using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageRequest : IRequest<SendMessageResponse>
{
    /// <summary>Smartsupp conversation id. Set from the route by the controller.</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>Text content to send as an agent message.</summary>
    public string Content { get; set; } = null!;
}
