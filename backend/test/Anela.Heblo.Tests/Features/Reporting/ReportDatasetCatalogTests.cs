using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportDatasetCatalogTests
{
    private static readonly string[] ForbiddenViews =
        ["v_posting", "v_payroll_monthly", "v_customer_repeat_purchase", "v_product_pair_monthly", "v_sync_health"];

    [Fact]
    public void Catalog_holds_the_seventeen_v1_datasets()
    {
        ReportDatasetCatalog.All.Should().HaveCount(17);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.WebAnalytics).Should().Be(5);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.Eshop).Should().Be(8);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.Accounting).Should().Be(4);
    }

    [Fact]
    public void Dataset_ids_are_unique_and_prefixed_by_source()
    {
        ReportDatasetCatalog.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        foreach (var d in ReportDatasetCatalog.All)
        {
            var prefix = d.Source switch
            {
                ReportSource.WebAnalytics => "ga4.",
                ReportSource.Eshop => "shoptet.",
                ReportSource.Accounting => "flexi.",
                _ => throw new InvalidOperationException(),
            };
            d.Id.Should().StartWith(prefix);
        }
    }

    [Fact]
    public void Every_referenced_column_exists_in_its_dataset()
    {
        foreach (var d in ReportDatasetCatalog.All)
        {
            d.FindColumn(d.PeriodColumn).Should().NotBeNull($"{d.Id} period column");
            d.FindColumn(d.PeriodColumn)!.Type.Should().Be(ReportColumnType.Date, d.Id);
            d.KeyColumns.Should().NotBeEmpty(d.Id);
            d.KeyColumns.Should().OnlyContain(k => d.FindColumn(k) != null, d.Id);
            d.DefaultSort.Should().NotBeEmpty(d.Id);
            d.DefaultSort.Should().OnlyContain(s => d.FindColumn(s.Column) != null, d.Id);
            d.Columns.Select(c => c.Name).Should().OnlyHaveUniqueItems(d.Id);
            d.Columns.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Description), d.Id);
        }
    }

    [Fact]
    public void Confidential_and_expensive_views_are_never_exposed()
    {
        ReportDatasetCatalog.All.Select(d => d.View).Should().NotContain(ForbiddenViews);
        ReportDatasetCatalog.All.Should().OnlyContain(d => d.View.StartsWith("v_"));
        ReportDatasetCatalog.All.Should().OnlyContain(d =>
            d.Schema == "ga4_agg" || d.Schema == "shoptet_raw" || d.Schema == "flexi_raw");
    }

    [Fact]
    public void Each_source_maps_to_its_own_feature()
    {
        ReportSources.FeatureFor(ReportSource.WebAnalytics).Should().Be(Feature.Reporting_WebAnalytics);
        ReportSources.FeatureFor(ReportSource.Eshop).Should().Be(Feature.Reporting_Eshop);
        ReportSources.FeatureFor(ReportSource.Accounting).Should().Be(Feature.Reporting_Accounting);
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown()
    {
        ReportDatasetCatalog.Find("SHOPTET.ORDERS_MONTHLY")!.View.Should().Be("v_order_monthly");
        ReportDatasetCatalog.Find("shoptet.nope").Should().BeNull();
    }
}
