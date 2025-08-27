using Anela.Heblo.Domain.Features.Logistics.Transport;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class ChangeTransportBoxStateRequest : IRequest<ChangeTransportBoxStateResponse>
{
    public int BoxId { get; set; }
    public TransportBoxState NewState { get; set; } 
    public string? Description { get; set; }
    public string? BoxCode { get; set; }
    public string? Location { get; set; }
}