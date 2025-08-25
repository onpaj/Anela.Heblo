using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetTransportBoxByIdRequest : IRequest<GetTransportBoxByIdResponse>
{
    public int Id { get; set; }
}