using MediatR;

namespace Anela.Heblo.Application.Features.Transport.UseCases.UpdateTransportBoxDescription;

public class UpdateTransportBoxDescriptionRequest : IRequest<UpdateTransportBoxDescriptionResponse>
{
    public int BoxId { get; set; }
    public string? Description { get; set; }
}