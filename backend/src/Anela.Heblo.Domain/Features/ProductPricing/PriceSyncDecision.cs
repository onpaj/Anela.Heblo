namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Outcome of the three-way compare for one product on one target.</summary>
public class PriceSyncDecision
{
    public PriceSyncAction Action { get; init; }

    /// <summary>Set only when <see cref="Action"/> is <see cref="PriceSyncAction.Push"/>.</summary>
    public decimal? PriceToPush { get; init; }

    /// <summary>The remote value, set for Conflict and Seed.</summary>
    public decimal? RemoteValue { get; init; }
}
