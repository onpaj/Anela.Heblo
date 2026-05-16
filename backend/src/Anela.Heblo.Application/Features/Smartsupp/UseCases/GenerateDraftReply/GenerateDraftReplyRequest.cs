using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyRequest : IRequest<GenerateDraftReplyResponse>
{
    /// <summary>Smartsupp conversation id. Set from the route by the controller.</summary>
    public string ConversationId { get; set; } = null!;

    /// <summary>Optional topic hint that steers KnowledgeBase retrieval and focus.</summary>
    public string? Topic { get; set; }
}
