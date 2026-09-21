using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsResponse : BaseResponse
{
    [Required] public List<MonthlyMarketingPerformanceDto> Months { get; set; } = new();
    [Required] public List<ChannelInfoDto> Channels { get; set; } = new();
    [Required] public string From { get; set; } = string.Empty;
    [Required] public string To { get; set; } = string.Empty;
    [Required] public bool IncludeWholesale { get; set; }
    [Required] public decimal VatRate { get; set; }
    public DateTime? LastRefreshAt { get; set; }

    public GetMarketingPerformanceMonthsResponse() { }
    public GetMarketingPerformanceMonthsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
