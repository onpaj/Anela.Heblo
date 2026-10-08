namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// <see cref="StaleState"/> is set by the core (spec 6.2) when ReadCurrentAsync disagrees with the
/// action's OldValue; executors themselves only ever return Succeeded or Failed.
/// </summary>
public enum AdExecutionOutcome
{
    Succeeded = 1,
    Failed = 2,
    StaleState = 3,
}
