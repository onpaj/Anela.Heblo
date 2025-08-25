using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class ChangeTransportBoxStateRequest : IRequest<ChangeTransportBoxStateResponse>
{
    public int BoxId { get; set; }
    public string NewState { get; set; } = null!;
    public string? Description { get; set; }
}