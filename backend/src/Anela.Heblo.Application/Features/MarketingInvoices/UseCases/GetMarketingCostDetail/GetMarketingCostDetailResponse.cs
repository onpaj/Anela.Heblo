using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailResponse : BaseResponse
{
    public MarketingCostDetailDto? Item { get; set; }
}
