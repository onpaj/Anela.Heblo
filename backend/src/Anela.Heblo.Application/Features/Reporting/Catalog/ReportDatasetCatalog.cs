namespace Anela.Heblo.Application.Features.Reporting.Catalog;

/// <summary>
/// The only relations reachable through the reporting tools. Each entry wraps one curated v_* read
/// view (the same views granted to metabase_ro). Never add raw tables, v_posting,
/// v_payroll_monthly or v_customer_repeat_purchase — see the spec, decision D7.
/// </summary>
public static partial class ReportDatasetCatalog
{
    public static IReadOnlyList<ReportDataset> All { get; } =
        [.. WebAnalytics(), .. Eshop(), .. Accounting()];

    public static ReportDataset? Find(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
