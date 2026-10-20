using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;

public class ExpireAdProposalsResponse : BaseResponse
{
    public int ExpiredCount { get; set; }
    public int RequeuedCount { get; set; }

    public ExpireAdProposalsResponse() { }

    public ExpireAdProposalsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
