using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoRequest : IRequest<GetVisitorInfoResponse>
{
    public required string ConversationId { get; set; }
}
