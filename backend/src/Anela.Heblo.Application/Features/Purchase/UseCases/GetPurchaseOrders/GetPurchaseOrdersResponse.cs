using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrders;

public class GetPurchaseOrdersResponse : BaseResponse
{
    public List<PurchaseOrderSummaryDto> Orders { get; set; } = null!;
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}