using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportingHandlersTests
{
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly Mock<IReportingDataReader> _reader = new();

    public ReportingHandlersTests()
    {
        _reader.Setup(r => r.GetFreshnessAsync(It.IsAny<ReportSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportFreshness { LastSyncAt = new DateTimeOffset(2026, 10, 13, 1, 0, 0, TimeSpan.Zero), Status = "OK" });
    }

    private void Grant(Feature feature) =>
        _user.Setup(u => u.IsInRole(AccessRoles.For(feature, AccessLevel.Read))).Returns(true);

    [Fact]
    public async Task List_omits_datasets_without_permission()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new ListReportDatasetsHandler(_user.Object, _reader.Object, NullLogger<ListReportDatasetsHandler>.Instance)
            .Handle(new ListReportDatasetsRequest(), default);

        result.Success.Should().BeTrue();
        result.Datasets.Should().HaveCount(8).And.OnlyContain(d => d.Id.StartsWith("shoptet."));
        result.Datasets[0].Freshness.Status.Should().Be("OK");
        _reader.Verify(r => r.GetFreshnessAsync(ReportSource.Eshop, It.IsAny<CancellationToken>()), Times.Once);
        _reader.Verify(r => r.GetFreshnessAsync(ReportSource.Accounting, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_is_empty_without_any_permission()
    {
        var result = await new ListReportDatasetsHandler(_user.Object, _reader.Object, NullLogger<ListReportDatasetsHandler>.Instance)
            .Handle(new ListReportDatasetsRequest(), default);

        result.Datasets.Should().BeEmpty();
    }

    [Fact]
    public async Task Describe_returns_columns_caveats_and_default_sort()
    {
        Grant(Feature.Reporting_WebAnalytics);

        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object, NullLogger<DescribeReportDatasetHandler>.Instance)
            .Handle(new DescribeReportDatasetRequest { Dataset = "ga4.conversion_monthly" }, default);

        result.Success.Should().BeTrue();
        result.Dataset!.Columns.Should().Contain(c => c.Name == "conversion_rate_pct" && c.Type == "Decimal");
        result.Dataset.Caveats.Should().Contain(c => c.Contains("WILL NOT MATCH THE ERP"));
        result.Dataset.DefaultSort.Should().Equal("month desc");
        result.Dataset.PeriodColumn.Should().Be("month");
    }

    [Fact]
    public async Task Describe_unknown_dataset_suggests_close_ids()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object, NullLogger<DescribeReportDatasetHandler>.Instance)
            .Handle(new DescribeReportDatasetRequest { Dataset = "shoptet.order_monthly" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ReportingDatasetNotFound);
        result.Params!["Suggestions"].Should().Contain("shoptet.orders_monthly");
    }

    [Fact]
    public async Task Describe_without_permission_is_forbidden()
    {
        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object, NullLogger<DescribeReportDatasetHandler>.Instance)
            .Handle(new DescribeReportDatasetRequest { Dataset = "flexi.cost_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Params!["RequiredRole"].Should().Be("reporting.accounting.read");
    }

    [Fact]
    public async Task Query_without_permission_is_forbidden_and_never_reaches_the_reader()
    {
        Grant(Feature.Reporting_WebAnalytics);

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance)
            .Handle(new QueryReportDatasetRequest { Dataset = "flexi.cost_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _reader.Verify(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Query_maps_request_to_sql_and_rows_to_response()
    {
        Grant(Feature.Reporting_Eshop);
        ReportSqlCommand? sent = null;
        _reader.Setup(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ReportSqlCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(new ReportRows { Rows = [[new DateOnly(2026, 9, 1), 42L]], HasMore = true });

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance).Handle(new QueryReportDatasetRequest
        {
            Dataset = "shoptet.orders_monthly",
            Columns = ["order_month", "orders"],
            Filters = [new ReportFilterDto { Column = "channel", Op = "eq", Value = "eshop_mo" }],
            Sort = [new ReportSortDto { Column = "orders", Direction = "desc" }],
            Limit = 1,
        }, default);

        result.Success.Should().BeTrue();
        sent!.Sql.Should().Contain("WHERE \"channel\" = @p0").And.Contain("ORDER BY \"orders\" DESC");
        result.Columns.Select(c => c.Name).Should().Equal("order_month", "orders");
        result.Rows.Should().ContainSingle().Which.Should().Equal(new DateOnly(2026, 9, 1), 42L);
        result.RowCount.Should().Be(1);
        result.HasMore.Should().BeTrue();
        result.Caveats.Should().NotBeEmpty();
        result.Freshness!.Status.Should().Be("OK");
    }

    [Fact]
    public async Task Query_validation_failure_returns_the_reason()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance)
            .Handle(new QueryReportDatasetRequest { Dataset = "shoptet.orders_monthly", Limit = 5000 }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingInvalidQuery);
        result.Params!["Reason"].Should().Contain("limit");
    }

    [Fact]
    public async Task Query_timeout_maps_to_ReportingQueryTimeout()
    {
        Grant(Feature.Reporting_Eshop);
        _reader.Setup(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReportQueryTimeoutException(new Exception("57014")));

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance)
            .Handle(new QueryReportDatasetRequest { Dataset = "shoptet.orders_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingQueryTimeout);
    }

    [Fact]
    public async Task Query_unknown_dataset_is_not_found_even_without_permissions()
    {
        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance)
            .Handle(new QueryReportDatasetRequest { Dataset = "flexi.payroll_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingDatasetNotFound);
    }

    [Fact]
    public async Task Query_succeeds_with_unknown_freshness_when_freshness_lookup_throws()
    {
        Grant(Feature.Reporting_Eshop);
        _reader.Setup(r => r.GetFreshnessAsync(It.IsAny<ReportSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync_state missing"));
        _reader.Setup(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportRows { Rows = [[new DateOnly(2026, 9, 1), 42L]], HasMore = false });

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object, NullLogger<QueryReportDatasetHandler>.Instance)
            .Handle(new QueryReportDatasetRequest { Dataset = "shoptet.orders_monthly", Columns = ["order_month", "orders"] }, default);

        result.Success.Should().BeTrue();
        result.Rows.Should().HaveCount(1);
        result.Freshness!.Status.Should().Be("UNKNOWN");
    }
}
