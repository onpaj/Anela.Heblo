using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetConversation;

public class GetConversationRequest : IRequest<GetConversationResponse>
{
    public string Id { get; set; } = null!;
}
