using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsResponse : BaseResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AdSearchTermRowDto> Terms { get; set; } = new();

    public GetAdSearchTermsResponse() { }
    public GetAdSearchTermsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
