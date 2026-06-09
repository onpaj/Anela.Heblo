using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderRequest : IRequest<ScanPackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
    public Guid? PackingUserId { get; set; }
}
