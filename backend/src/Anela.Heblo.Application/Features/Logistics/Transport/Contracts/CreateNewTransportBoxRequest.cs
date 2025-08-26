using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class CreateNewTransportBoxRequest : IRequest<CreateNewTransportBoxResponse>
{
    public string? Description { get; set; }
}