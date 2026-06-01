namespace Anela.Heblo.Adapters.GoogleAds;

/// <summary>Abstracts Google Ads SDK GAQL execution for testability.</summary>
internal interface IAccountBudgetFetcher
{
    Task<IReadOnlyList<RawAccountBudget>> FetchAsync(DateTime from, DateTime to, CancellationToken ct);
}
