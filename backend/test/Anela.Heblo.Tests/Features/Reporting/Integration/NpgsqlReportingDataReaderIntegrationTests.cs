using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class NpgsqlReportingDataReaderIntegrationTests
{
    private readonly PostgresSharedContainerFixture _fixture;

    public NpgsqlReportingDataReaderIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    // A stand-in for shoptet_raw.v_order_monthly with the same schema/view names and three months.
    private const string Seed = """
        CREATE SCHEMA shoptet_raw;
        CREATE TABLE shoptet_raw.t (order_month date, channel text, orders bigint, customers bigint,
            revenue_with_vat_czk numeric, revenue_without_vat_czk numeric, product_revenue_with_vat_czk numeric,
            shipping_charged_with_vat_czk numeric, payment_fee_with_vat_czk numeric, discount_with_vat_czk numeric,
            units numeric, avg_order_value_with_vat_czk numeric, avg_basket_units numeric);
        INSERT INTO shoptet_raw.t VALUES
            ('2026-07-01','eshop_mo',10,9,1000.50,826.86,900,50,0,0,30,100.05,3),
            ('2026-08-01','eshop_mo',20,18,2000,1652.89,1800,100,0,0,60,100,3),
            ('2026-09-01','eshop_vo',5,2,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
        CREATE VIEW shoptet_raw.v_order_monthly AS SELECT * FROM shoptet_raw.t;
        CREATE TABLE shoptet_raw.sync_state (entity_name text, last_run_finished_at timestamptz, last_run_status text);
        INSERT INTO shoptet_raw.sync_state VALUES ('order', '2026-10-12 23:32:41+00', 'OK'), ('order_backfill', '2026-09-22 21:22:42+00', 'OK');
        CREATE SCHEMA flexi_raw;
        CREATE TABLE flexi_raw.sync_state (entity_name text, last_run_finished_at timestamptz, last_run_status text);
        INSERT INTO flexi_raw.sync_state VALUES ('ledger_entry', '2026-10-13 01:00:21+00', 'OK'), ('contact', '2026-10-13 01:00:22+00', 'FAILED');
        """;

    private async Task<NpgsqlDataSource> CreateAsync(string hint)
    {
        var cs = await _fixture.CreateDatabaseAsync(hint);
        var ds = NpgsqlDataSource.Create(cs);
        await using var cmd = ds.CreateCommand(Seed);
        await cmd.ExecuteNonQueryAsync();
        return ds;
    }

    private static readonly ReportDataset Orders = ReportDatasetCatalog.Find("shoptet.orders_monthly")!;

    [Fact]
    public async Task Returns_typed_rows_and_detects_a_further_page()
    {
        await using var ds = await CreateAsync("rep_rows");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery
        {
            Columns = ["order_month", "orders", "revenue_with_vat_czk", "channel"],
            Filters = [new ReportFilter { Column = "order_month", Op = "gte", Value = "2026-07" }],
            Limit = 2,
        });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.HasMore.Should().BeTrue();
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Should().Equal(new DateOnly(2026, 9, 1), 5L, null, "eshop_vo");
        result.Rows[1].Should().Equal(new DateOnly(2026, 8, 1), 20L, 2000m, "eshop_mo");
    }

    [Fact]
    public async Task Last_page_reports_no_more_rows()
    {
        await using var ds = await CreateAsync("rep_last");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery { Limit = 2, Offset = 2 });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.HasMore.Should().BeFalse();
        result.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task In_and_contains_filters_bind_against_a_real_server()
    {
        await using var ds = await CreateAsync("rep_filters");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery
        {
            Columns = ["channel"],
            Filters =
            [
                new ReportFilter { Column = "channel", Op = "in", Values = ["eshop_mo", "prodejna"] },
                new ReportFilter { Column = "channel", Op = "contains", Value = "SHOP_m" },
            ],
        });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.Rows.Should().HaveCount(2).And.OnlyContain(r => (string)r[0]! == "eshop_mo");
    }

    [Fact]
    public async Task Runs_in_a_read_only_transaction()
    {
        await using var ds = await CreateAsync("rep_readonly");
        var command = new ReportSqlCommand
        {
            Sql = "INSERT INTO shoptet_raw.t (channel) VALUES ('x') RETURNING channel",
            Parameters = [],
            OutputColumns = [ReportColumn.Text("channel", "c")],
            Limit = 1,
        };

        var act = () => new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25006"); // read_only_sql_transaction
    }

    [Fact]
    public async Task Statement_timeout_raises_ReportQueryTimeoutException()
    {
        await using var ds = await CreateAsync("rep_timeout");
        var command = new ReportSqlCommand
        {
            Sql = "SELECT pg_sleep(5)::text AS x",
            Parameters = [],
            OutputColumns = [ReportColumn.Text("x", "x")],
            Limit = 1,
        };

        var act = () => new NpgsqlReportingDataReader(ds, TimeSpan.FromSeconds(1)).QueryAsync(command, default);

        await act.Should().ThrowAsync<ReportQueryTimeoutException>();
    }

    [Fact]
    public async Task Freshness_reads_the_source_sync_state()
    {
        await using var ds = await CreateAsync("rep_fresh");
        var reader = new NpgsqlReportingDataReader(ds);

        var shoptet = await reader.GetFreshnessAsync(ReportSource.Eshop, default);
        var flexi = await reader.GetFreshnessAsync(ReportSource.Accounting, default);

        shoptet.Status.Should().Be("OK");
        shoptet.LastSyncAt.Should().Be(new DateTimeOffset(2026, 10, 12, 23, 32, 41, TimeSpan.Zero));
        flexi.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Freshness_is_unknown_when_the_schema_is_missing()
    {
        await using var ds = await CreateAsync("rep_fresh_missing");

        var ga4 = await new NpgsqlReportingDataReader(ds).GetFreshnessAsync(ReportSource.WebAnalytics, default);

        ga4.Status.Should().Be("UNKNOWN");
        ga4.LastSyncAt.Should().BeNull();
    }
}
