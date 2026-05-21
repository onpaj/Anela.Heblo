using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoRequest : IRequest<GetSmartsuppContactShoptetInfoResponse>
{
    public required string ConversationId { get; set; }
}
