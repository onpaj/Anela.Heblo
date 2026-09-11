namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>
/// Three-way compare between Heblo's price, the value Heblo last pushed, and the
/// value currently in the remote system.
///
/// Comparing Heblo to the remote only tells you *that* they differ. Comparing both
/// against the last pushed value tells you *who moved*, which is what lets a
/// downstream edit stop the sync instead of being silently overwritten.
/// </summary>
public static class PriceSyncDecider
{
    private const int PriceDecimals = 2;

    public static PriceSyncDecision Decide(
        decimal hebloPriceWithVat,
        decimal? lastPushedPriceWithVat,
        decimal? remotePriceWithVat,
        decimal remoteTolerance = 0m)
    {
        if (remotePriceWithVat is null)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.MissingRemote };
        }

        var remote = Normalize(remotePriceWithVat.Value);

        if (lastPushedPriceWithVat is null)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Seed, RemoteValue = remote };
        }

        var heblo = Normalize(hebloPriceWithVat);
        var lastPushed = Normalize(lastPushedPriceWithVat.Value);

        // Remote drift is checked first: when both sides moved it is still a conflict,
        // and a human decides which wins. A target with a lossy round-trip (e.g. Flexi
        // reconstructing a with-VAT price from a without-VAT store) supplies a small
        // tolerance so its own rounding error never reads as a downstream edit.
        if (Math.Abs(remote - lastPushed) > remoteTolerance)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Conflict, RemoteValue = remote };
        }

        if (heblo != lastPushed)
        {
            return new PriceSyncDecision { Action = PriceSyncAction.Push, PriceToPush = heblo };
        }

        return new PriceSyncDecision { Action = PriceSyncAction.None };
    }

    private static decimal Normalize(decimal value) =>
        Math.Round(value, PriceDecimals, MidpointRounding.AwayFromZero);
}
