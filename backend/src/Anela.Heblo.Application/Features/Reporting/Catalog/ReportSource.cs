using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public enum ReportSource
{
    WebAnalytics,
    Eshop,
    Accounting,
}

public static class ReportSources
{
    // Each returns (last_sync_at timestamptz, all_ok boolean) from the source's own sync_state.
    private const string Ga4FreshnessSql =
        "SELECT min(last_run_finished_at), bool_and(last_run_status = 'OK') FROM ga4_agg.sync_state";
    private const string ShoptetFreshnessSql =
        "SELECT max(last_run_finished_at), bool_and(last_run_status = 'OK') FROM shoptet_raw.sync_state WHERE entity_name = 'order'";
    private const string FlexiFreshnessSql =
        "SELECT min(last_run_finished_at), bool_and(last_run_status = 'OK') FROM flexi_raw.sync_state";

    public static Feature FeatureFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => Feature.Reporting_WebAnalytics,
        ReportSource.Eshop => Feature.Reporting_Eshop,
        ReportSource.Accounting => Feature.Reporting_Accounting,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static string LabelFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => "Google Analytics 4",
        ReportSource.Eshop => "Shoptet e-shop orders",
        ReportSource.Accounting => "ABRA Flexi accounting",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static string FreshnessSqlFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => Ga4FreshnessSql,
        ReportSource.Eshop => ShoptetFreshnessSql,
        ReportSource.Accounting => FlexiFreshnessSql,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
