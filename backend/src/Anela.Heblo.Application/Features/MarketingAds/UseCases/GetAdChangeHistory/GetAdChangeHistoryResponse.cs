using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<AdChangeEventDto> Events { get; set; } = new();

    public GetAdChangeHistoryResponse() { }
    public GetAdChangeHistoryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
