using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ReceiveTransportBox;

public class ReceiveTransportBoxRequest : IRequest<ReceiveTransportBoxResponse>
{
    public int BoxId { get; set; }
    public string UserName { get; set; } = string.Empty;
}