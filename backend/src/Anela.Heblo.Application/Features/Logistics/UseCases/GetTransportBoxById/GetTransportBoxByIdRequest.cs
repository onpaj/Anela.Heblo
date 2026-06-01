using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;

public class GetTransportBoxByIdRequest : IRequest<GetTransportBoxByIdResponse>
{
    public int Id { get; set; }
}