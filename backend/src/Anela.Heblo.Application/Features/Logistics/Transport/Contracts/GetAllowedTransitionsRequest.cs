using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetAllowedTransitionsRequest : IRequest<GetAllowedTransitionsResponse>
{
    public int BoxId { get; set; }
}