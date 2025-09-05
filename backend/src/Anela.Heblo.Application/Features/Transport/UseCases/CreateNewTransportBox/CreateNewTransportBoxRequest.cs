using MediatR;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class CreateNewTransportBoxRequest : IRequest<CreateNewTransportBoxResponse>
{
    public string? Description { get; set; }
}