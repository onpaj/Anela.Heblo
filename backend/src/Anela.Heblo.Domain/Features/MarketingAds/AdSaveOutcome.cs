namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Result of <see cref="IAdProposalRepository.TrySaveChangesWithOutcomeAsync"/>.</summary>
public enum AdSaveOutcome
{
    Saved = 1,
    /// <summary>Another writer changed the row first (optimistic concurrency token) or inserted the same unique key.</summary>
    ConcurrentChange = 2,
    /// <summary>Any other failed update (constraint, mapping, connection).</summary>
    Failed = 3,
}
