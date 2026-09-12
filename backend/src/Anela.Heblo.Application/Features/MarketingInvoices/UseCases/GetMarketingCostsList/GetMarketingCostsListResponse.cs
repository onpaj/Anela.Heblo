using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListResponse : BaseResponse
{
    public List<MarketingCostListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
