using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string FlexiSchema = "flexi_raw";
    private const string CostCenterDesc = "Cost centre (středisko): BUVOL, C, MARKETING, PRODEJNA, SKLAD, VYROBA.";
    private const string NetCostDesc = "Net cost in CZK (debits minus credits), excl. VAT.";
    private const string PostingCountDesc = "Number of ledger postings behind the figure.";
    private const string CostScope = "Costs are accounts of classes 50–56 only (58x contra-costs and 59x income tax excluded) and EXCLUDE personnel/payroll cost, which is not available here.";
    private const string LedgerSource = "Source: ABRA Flexi general ledger, synced nightly; history from 2020-01. The latest month is still being booked.";

    private static IEnumerable<ReportDataset> Accounting()
    {
        yield return new ReportDataset
        {
            Id = "flexi.cost_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_cost_monthly_total",
            Description = "Company operating cost per month and cost centre.",
            Grain = "month × cost centre",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("cost_center", CostCenterDesc),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "cost_center"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats = [CostScope, LedgerSource],
        };

        yield return new ReportDataset
        {
            Id = "flexi.cost_by_account_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_cost_monthly_by_account",
            Description = "Operating cost per month, cost centre and 6-digit account (drill-down of flexi.cost_monthly).",
            Grain = "month × cost centre × account",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("cost_center", CostCenterDesc),
                Text("account", "6-digit account number, e.g. 504000."),
                Text("account_name", "Account name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "cost_center", "account"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                CostScope,
                LedgerSource,
                "Goods sold (504) and material consumption (501) are both class 50 — group by account, not by a '50' prefix.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "flexi.marketing_spend_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_marketing_spend_monthly",
            Description = "Marketing spend per month by account, category, cost centre and supplier.",
            Grain = "month × account × category × cost centre × supplier",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("account", "6-digit account number."),
                Text("account_name", "Account name."),
                Text("category", "Marketing category: performance, brand, externiste, darky, trhy, vyrobky-na-reklamu, ostatni."),
                Text("cost_center", CostCenterDesc),
                Text("supplier", "Supplier (contact) name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "account", "category", "cost_center", "supplier"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                LedgerSource,
                "Grouped by account, not by accounting template (the ledger carries no předkontace). Graphics, photography, PR and influencers share account 518030 — only supplier tells them apart.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "flexi.ad_spend_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_ad_spend_monthly",
            Description = "Booked ad spend per month and channel (meta, google, sklik), from supplier invoices.",
            Grain = "month × channel × supplier × account",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("channel", "Ad channel: meta, google, sklik."),
                Text("supplier", "Supplier (contact) name."),
                Text("account", "6-digit account number."),
                Text("account_name", "Account name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "channel", "supplier", "account"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                LedgerSource,
                "Channel is matched on the supplier label, not on a key. Booking date follows the invoice, which can lag the ad platform's own spend by a month.",
            ],
        };
    }
}
