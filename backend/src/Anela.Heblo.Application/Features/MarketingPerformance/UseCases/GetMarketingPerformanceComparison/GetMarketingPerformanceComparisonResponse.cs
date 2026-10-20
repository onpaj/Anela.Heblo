using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonResponse : BaseResponse
{
    /// <summary>Newest year first.</summary>
    [Required] public List<MarketingYearSeriesDto> Series { get; set; } = new();
    [Required] public int AnchorYear { get; set; }
    /// <summary>Current calendar month 1..12 (partial).</summary>
    [Required] public int CurrentMonth { get; set; }
    [Required] public List<ChannelInfoDto> Channels { get; set; } = new();
    [Required] public bool IncludeWholesale { get; set; }
    public DateTime? LastRefreshAt { get; set; }
}
