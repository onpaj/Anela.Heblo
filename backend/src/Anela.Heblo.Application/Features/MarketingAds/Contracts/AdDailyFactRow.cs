namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Metrics for one entity on one day. Reported at every level the platform reports, so consumers
/// must aggregate a single level (summing levels double-counts). Cost is net of VAT, in account currency.
/// </summary>
public sealed record AdDailyFactRow(
    AdEntityLevel Level, string EntityExternalId, DateOnly Date, long Impressions, long Clicks,
    decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
