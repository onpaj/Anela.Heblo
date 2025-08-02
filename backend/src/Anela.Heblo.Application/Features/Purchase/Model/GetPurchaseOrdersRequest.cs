using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseOrdersRequest : IRequest<GetPurchaseOrdersResponse>
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? SupplierId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "OrderDate";
    public bool SortDescending { get; set; } = true;
}