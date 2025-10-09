using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.CreateNewTransportBox;

public class CreateNewTransportBoxRequest : IRequest<CreateNewTransportBoxResponse>
{
    public string? Description { get; set; }
}