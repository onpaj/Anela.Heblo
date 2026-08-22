using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailRequest : IRequest<GetMarketingCostDetailResponse>
{
    public int Id { get; set; }
}
