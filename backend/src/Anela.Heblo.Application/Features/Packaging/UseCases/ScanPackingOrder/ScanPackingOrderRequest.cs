using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;

public class ScanPackingOrderRequest : IRequest<ScanPackingOrderResponse>
{
    public string OrderCode { get; set; } = null!;
    public int NumberOfPackages { get; set; } = 1;
    public Guid? PackingUserId { get; set; }
}
