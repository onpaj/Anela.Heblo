using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsResponse : BaseResponse
{
    public List<AdAccountDto> Accounts { get; set; } = new();

    public GetAdAccountsResponse() { }
    public GetAdAccountsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
