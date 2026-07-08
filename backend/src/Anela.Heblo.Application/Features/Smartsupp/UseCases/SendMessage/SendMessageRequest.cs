using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageRequest : IRequest<SendMessageResponse>
{
    /// <summary>Smartsupp conversation id. Set from the route by the controller.</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>Text content to send as an agent message.</summary>
    public string Content { get; set; } = null!;

    /// <summary>
    /// When the message originated from an AI draft, the id of that draft's RAG interaction log row.
    /// Lets the backend record the actually-sent text and whether it was edited before sending.
    /// </summary>
    public Guid? DraftLogId { get; set; }
}
