namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>Cost step of the snapshot: received invoices for one month whose supplier DIČ is in <paramref name="vatIds"/>.</summary>
public interface IMonthlyAdCostSource
{
    Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken);

    /// <summary>
    /// True when this source is wired to a real backend (e.g. the Flexi adapter) whose results — including an
    /// empty list — are trustworthy. Defaults to true via this default interface member so any future real
    /// implementation needs no explicit opt-in; only a placeholder/no-op source overrides it to false, so the
    /// refresh service can tell "genuinely zero cost" apart from "cost source not wired yet".
    /// </summary>
    bool IsConfigured => true;
}
