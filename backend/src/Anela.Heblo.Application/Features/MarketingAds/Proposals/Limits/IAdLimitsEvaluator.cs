namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Pure, server-side limits (spec 6.3). Independent of who approves.</summary>
public interface IAdLimitsEvaluator
{
    AdLimitsEvaluation Evaluate(AdLimitsRequest request);
}
