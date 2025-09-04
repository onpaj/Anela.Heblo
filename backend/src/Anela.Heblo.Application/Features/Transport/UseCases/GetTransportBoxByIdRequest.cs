using MediatR;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class GetTransportBoxByIdRequest : IRequest<GetTransportBoxByIdResponse>
{
    public int Id { get; set; }
}