using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsRequest : IRequest<GetMarketingPerformanceMonthsResponse>
{
    /// <summary>"yyyy-MM"; default = 35 months before the current month.</summary>
    public string? From { get; set; }
    /// <summary>"yyyy-MM"; default = current month.</summary>
    public string? To { get; set; }
    /// <summary>Add wholesale (customer with VAT ID) orders and revenue to the retail figures.</summary>
    public bool IncludeWholesale { get; set; }
}
