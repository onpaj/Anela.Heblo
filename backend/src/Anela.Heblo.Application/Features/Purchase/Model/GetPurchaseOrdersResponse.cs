namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseOrdersResponse
{
    public List<PurchaseOrderSummaryDto> Orders { get; set; } = null!;
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}