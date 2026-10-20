using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string Ga4Schema = "ga4_agg";
    private const string MonthDesc = "Calendar month (first day of the month).";
    private const string PartialMonth = "The current month is partial — check is_partial_month before comparing it with full months.";

    private static IEnumerable<ReportDataset> WebAnalytics()
    {
        yield return new ReportDataset
        {
            Id = "ga4.traffic_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_traffic",
            Description = "Total anela.cz website traffic per month with month-over-month and year-over-year comparison.",
            Grain = "month",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("sessions", "Sessions in the month."),
                Int("total_users", "GA4's own de-duplicated monthly users (never sum daily users)."),
                Int("new_users", "First-time users in the month."),
                Int("page_views", "Screen/page views."),
                Int("engaged_sessions", "Sessions GA4 counts as engaged."),
                Dec("bounce_rate", "Share of non-engaged sessions, 0–1."),
                Dec("avg_engagement_seconds_per_session", "Average engagement time per session, seconds."),
                Int("days_with_data", "Days of the month present in the data."),
                Int("sessions_prev_month", "Sessions in the previous month."),
                Dec("sessions_mom_pct", "Month-over-month change of sessions, percent."),
                Int("sessions_same_month_last_year", "Sessions in the same month a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change of sessions, percent."),
                Int("total_users_same_month_last_year", "Users in the same month a year earlier."),
                Dec("total_users_yoy_pct", "Year-over-year change of users, percent."),
                Bool("is_partial_month", "True for the month still in progress."),
            ],
            KeyColumns = ["month"],
            DefaultSort = [("month", true)],
            Caveats =
            [
                "Matches the GA4 UI. Do not rebuild these totals from ga4.traffic_by_channel_monthly: GA4 totals do not decompose.",
                "History starts 2023-06 (GA4 property creation).",
                PartialMonth,
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.traffic_by_channel_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_traffic_by_channel",
            Description = "Sessions per GA4 default channel group (Organic Search, Paid Social, Direct, …) per month.",
            Grain = "month × channel group",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("channel_group", "GA4 default channel group."),
                Dec("sessions", "Sessions from this channel."),
                Dec("channel_users", "Users from this channel (do not sum across channels or days)."),
                Dec("channel_new_users", "New users from this channel."),
                Dec("engaged_sessions", "Engaged sessions from this channel."),
                Dec("pct_of_month_sessions", "Share of the month's sessions, percent."),
                Dec("sessions_prev_month", "Sessions from this channel in the previous month."),
                Dec("sessions_same_month_last_year", "Sessions from this channel a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "channel_group"],
            DefaultSort = [("month", true), ("sessions", true)],
            Caveats =
            [
                "Use for the shape of acquisition, not for totals: channel sessions sum 0.0–3.8% above the site total. Totals are in ga4.traffic_monthly.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.landing_pages_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_landing_pages",
            Description = "Landing pages ranked by sessions per month, with previous-month and year-ago rank and sessions.",
            Grain = "month × landing page",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("rank_in_month", "Rank by sessions within the month, 1 = most sessions."),
                Text("landing_page", "Landing page path."),
                Dec("sessions", "Sessions that started on this page."),
                Dec("engaged_sessions", "Engaged sessions that started on this page."),
                Dec("bounce_rate", "Share of non-engaged sessions, 0–1."),
                Dec("sessions_prev_month", "Sessions in the previous month."),
                Int("rank_prev_month", "Rank in the previous month."),
                Dec("sessions_same_month_last_year", "Sessions a year earlier."),
                Int("rank_same_month_last_year", "Rank a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "landing_page"],
            DefaultSort = [("month", true), ("rank_in_month", false)],
            Caveats =
            [
                "Built from a top-100-pages-per-day capture: the head of the ranking is exact, the sum of sessions is not a site total.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.articles_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_articles",
            Description = "Pages ranked by page views per month (filter page_path, e.g. contains '/blog', for articles).",
            Grain = "month × page",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("rank_in_month", "Rank by page views within the month, 1 = most viewed."),
                Text("page_path", "Page path."),
                Text("page_title", "Most frequent page title for the path."),
                Dec("page_views", "Page views."),
                Dec("sessions", "Sessions that viewed the page."),
                Dec("page_views_prev_month", "Page views in the previous month."),
                Int("rank_prev_month", "Rank in the previous month."),
                Dec("page_views_same_month_last_year", "Page views a year earlier."),
                Int("rank_same_month_last_year", "Rank a year earlier."),
                Dec("page_views_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "page_path"],
            DefaultSort = [("month", true), ("rank_in_month", false)],
            Caveats = ["Only the top 100 pages per day are captured."],
        };

        yield return new ReportDataset
        {
            Id = "ga4.conversion_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_conversion",
            Description = "GA4 sessions against GA4 purchase events per month (website conversion trend).",
            Grain = "month",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("sessions", "Sessions in the month."),
                Dec("transactions", "GA4 purchase events."),
                Dec("purchase_revenue", "Revenue reported by GA4 purchase events."),
                Dec("conversion_rate_pct", "transactions / sessions, percent."),
                Dec("avg_order_value", "purchase_revenue / transactions."),
                Int("sessions_prev_month", "Sessions in the previous month."),
                Dec("transactions_prev_month", "Purchase events in the previous month."),
                Dec("conversion_rate_pct_prev_month", "Conversion rate in the previous month, percent."),
                Int("sessions_same_month_last_year", "Sessions a year earlier."),
                Dec("transactions_same_month_last_year", "Purchase events a year earlier."),
                Dec("conversion_rate_pct_same_month_last_year", "Conversion rate a year earlier, percent."),
                Dec("transactions_yoy_pct", "Year-over-year change of purchase events, percent."),
                Bool("is_partial_month", "True for the month still in progress."),
            ],
            KeyColumns = ["month"],
            DefaultSort = [("month", true)],
            Caveats =
            [
                "WILL NOT MATCH THE ERP: GA4 purchase events count cancelled and unpaid orders, can double-fire on a thank-you-page reload, and miss phone/e-mail orders and consent-blocked shoppers. Valid as a website trend, never as the company conversion rate — use shoptet.orders_monthly for real orders and revenue.",
                PartialMonth,
            ],
        };
    }
}
