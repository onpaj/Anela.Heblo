using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListRequest : IRequest<GetMarketingCostsListResponse>
{
    public string? Platform { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? IsSynced { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}
