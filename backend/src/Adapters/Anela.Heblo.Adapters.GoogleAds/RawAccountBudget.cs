namespace Anela.Heblo.Adapters.GoogleAds;

/// <summary>Raw data returned from the Google Ads account_budget GAQL query.</summary>
internal sealed record RawAccountBudget(
    string Id,
    string? Name,
    DateTime StartDate,
    long AmountServedMicros,
    string CurrencyCode);
