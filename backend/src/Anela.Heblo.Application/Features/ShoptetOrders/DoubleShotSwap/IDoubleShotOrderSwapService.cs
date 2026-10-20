namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

public interface IDoubleShotOrderSwapService
{
    /// <summary>
    /// Finds recent orders still containing a double-shot V line and replaces each V line with its
    /// S set (POST S, then DELETE V). Orders in the hold status are swapped first and, once they hold
    /// no V line, released to the release status. With <paramref name="applyChanges"/> false it only logs.
    /// </summary>
    Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default);
}

public sealed record DoubleShotSwapSummary(
    int OrdersScanned,
    int LinesSwapped,
    int LinesHealed,
    int LinesFailed,
    int OrdersFailed,
    int OrdersNotEligible = 0,
    bool SkippedBusy = false,
    int ListingsFailed = 0,
    int OrdersReleased = 0,
    int OrdersStillHeld = 0)
{
    public static DoubleShotSwapSummary Busy { get; } = new(0, 0, 0, 0, 0, SkippedBusy: true);
}
