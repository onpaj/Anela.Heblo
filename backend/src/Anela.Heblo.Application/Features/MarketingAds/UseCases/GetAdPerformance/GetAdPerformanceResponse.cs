using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceResponse : BaseResponse
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public AdEntityLevel Level { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<AdPerformanceRowDto> Rows { get; set; } = new();

    public GetAdPerformanceResponse() { }
    public GetAdPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
