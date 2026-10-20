namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Outcome of one action of an executed proposal version.</summary>
public enum AdActionExecutionState
{
    Succeeded = 1,
    Failed = 2,
    StaleState = 3,
    Skipped = 4,
}
