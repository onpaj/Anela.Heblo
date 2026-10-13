using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;
using FluentAssertions;
using NpgsqlTypes;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportSqlBuilderTests
{
    private static readonly ReportDataset Orders = ReportDatasetCatalog.Find("shoptet.orders_monthly")!;
    private static readonly ReportDataset Articles = ReportDatasetCatalog.Find("ga4.articles_monthly")!;

    private static ReportSqlCommand Build(ReportQuery query, ReportDataset? dataset = null) =>
        ReportSqlBuilder.Build(dataset ?? Orders, query);

    private static ReportFilter F(string column, string op, string? value = null, params string[] values) =>
        new() { Column = column, Op = op, Value = value, Values = values.Length > 0 ? values : null };

    [Fact]
    public void Default_query_selects_all_columns_with_default_sort_and_limit_plus_one()
    {
        var cmd = Build(new ReportQuery());

        cmd.Sql.Should().StartWith("SELECT \"order_month\", \"channel\", \"orders\"");
        cmd.Sql.Should().Contain("FROM \"shoptet_raw\".\"v_order_monthly\"");
        cmd.Sql.Should().NotContain("WHERE");
        cmd.Sql.Should().Contain("ORDER BY \"order_month\" DESC NULLS LAST, \"channel\" ASC NULLS LAST");
        cmd.Sql.Should().EndWith("LIMIT @limit OFFSET @offset");
        cmd.Limit.Should().Be(100);
        cmd.Parameters.Single(p => p.ParameterName == "limit").Value.Should().Be(101);
        cmd.Parameters.Single(p => p.ParameterName == "offset").Value.Should().Be(0);
        cmd.OutputColumns.Should().HaveCount(Orders.Columns.Count);
    }

    [Fact]
    public void Column_projection_keeps_requested_order_and_canonical_names()
    {
        var cmd = Build(new ReportQuery { Columns = ["Revenue_With_Vat_Czk", "order_month"] });

        cmd.Sql.Should().StartWith("SELECT \"revenue_with_vat_czk\", \"order_month\" FROM");
        cmd.OutputColumns.Select(c => c.Name).Should().Equal("revenue_with_vat_czk", "order_month");
    }

    [Theory]
    [InlineData("eq", "=")]
    [InlineData("gt", ">")]
    [InlineData("gte", ">=")]
    [InlineData("lt", "<")]
    [InlineData("lte", "<=")]
    public void Comparison_ops_bind_a_typed_parameter(string op, string sqlOp)
    {
        var cmd = Build(new ReportQuery { Filters = [F("orders", op, "10")] });

        cmd.Sql.Should().Contain($"WHERE \"orders\" {sqlOp} @p0");
        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Bigint);
        p.Value.Should().Be(10L);
    }

    [Fact]
    public void Neq_is_null_safe()
    {
        Build(new ReportQuery { Filters = [F("channel", "neq", "prodejna")] })
            .Sql.Should().Contain("\"channel\" IS DISTINCT FROM @p0");
    }

    [Fact]
    public void In_binds_an_array_parameter()
    {
        var cmd = Build(new ReportQuery { Filters = [F("channel", "in", null, "eshop_mo", "eshop_vo")] });

        cmd.Sql.Should().Contain("\"channel\" = ANY(@p0)");
        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Array | NpgsqlDbType.Text);
        p.Value.Should().BeEquivalentTo(new[] { "eshop_mo", "eshop_vo" });
    }

    [Fact]
    public void Between_binds_two_parameters_inclusive()
    {
        var cmd = Build(new ReportQuery { Filters = [F("order_month", "between", null, "2026-01-01", "2026-06-01")] });

        cmd.Sql.Should().Contain("\"order_month\" BETWEEN @p0 AND @p1");
        cmd.Parameters.Single(x => x.ParameterName == "p0").Value.Should().Be(new DateOnly(2026, 1, 1));
        cmd.Parameters.Single(x => x.ParameterName == "p1").Value.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Date_accepts_year_month_shorthand()
    {
        var cmd = Build(new ReportQuery { Filters = [F("order_month", "gte", "2026-09")] });

        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Date);
        p.Value.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Contains_escapes_like_wildcards()
    {
        var cmd = Build(new ReportQuery { Filters = [F("page_path", "contains", @"50%_off\x")] }, Articles);

        cmd.Sql.Should().Contain("\"page_path\" ILIKE '%' || @p0 || '%' ESCAPE '\\'");
        cmd.Parameters.Single(x => x.ParameterName == "p0").Value.Should().Be(@"50\%\_off\\x");
    }

    [Theory]
    [InlineData("true", "IS NULL")]
    [InlineData("false", "IS NOT NULL")]
    public void IsNull_takes_a_boolean(string value, string expected)
    {
        var cmd = Build(new ReportQuery { Filters = [F("variant_name", "isNull", value)] },
            ReportDatasetCatalog.Find("shoptet.product_sales_monthly")!);

        cmd.Sql.Should().Contain($"\"variant_name\" {expected}");
        cmd.Parameters.Should().NotContain(p => p.ParameterName == "p0");
    }

    [Fact]
    public void Multiple_filters_are_anded_with_increasing_parameter_names()
    {
        var cmd = Build(new ReportQuery { Filters = [F("channel", "eq", "eshop_mo"), F("orders", "gt", "5")] });

        cmd.Sql.Should().Contain("WHERE \"channel\" = @p0 AND \"orders\" > @p1");
    }

    [Fact]
    public void Key_columns_are_appended_as_tiebreak_once()
    {
        var cmd = Build(new ReportQuery { Sort = [new ReportSort { Column = "orders", Direction = "desc" }, new ReportSort { Column = "channel", Direction = "asc" }] });

        cmd.Sql.Should().Contain("ORDER BY \"orders\" DESC NULLS LAST, \"channel\" ASC NULLS LAST, \"order_month\" ASC NULLS LAST LIMIT");
    }

    [Fact]
    public void Limit_and_offset_are_honoured()
    {
        var cmd = Build(new ReportQuery { Limit = 1000, Offset = 2000 });

        cmd.Limit.Should().Be(1000);
        cmd.Parameters.Single(p => p.ParameterName == "limit").Value.Should().Be(1001);
        cmd.Parameters.Single(p => p.ParameterName == "offset").Value.Should().Be(2000);
    }

    public static TheoryData<ReportQuery, string> Invalid => new()
    {
        { new ReportQuery { Columns = ["nope"] }, "Unknown column 'nope'" },
        { new ReportQuery { Columns = [] }, "columns" },
        { new ReportQuery { Filters = [F("nope", "eq", "1")] }, "Unknown column 'nope'" },
        { new ReportQuery { Filters = [F("orders", "like", "1")] }, "Unknown operator 'like'" },
        { new ReportQuery { Filters = [F("orders", "eq", "abc")] }, "not a valid integer" },
        { new ReportQuery { Filters = [F("order_month", "eq", "13/2026")] }, "not a valid date" },
        { new ReportQuery { Filters = [F("orders", "eq")] }, "requires 'value'" },
        { new ReportQuery { Filters = [F("channel", "gt", "a")] }, "not allowed on Text" },
        { new ReportQuery { Filters = [F("orders", "contains", "1")] }, "only allowed on Text" },
        { new ReportQuery { Filters = [F("order_month", "between", null, "2026-01-01")] }, "exactly 2" },
        { new ReportQuery { Filters = [F("channel", "in")] }, "between 1 and 100" },
        { new ReportQuery { Filters = [F("channel", "isNull", "maybe")] }, "true or false" },
        { new ReportQuery { Filters = Enumerable.Repeat(F("orders", "gt", "1"), 21).ToList() }, "at most 20 filters" },
        { new ReportQuery { Sort = [new ReportSort { Column = "orders", Direction = "up" }] }, "asc or desc" },
        { new ReportQuery { Sort = Enumerable.Repeat(new ReportSort { Column = "orders", Direction = "asc" }, 6).ToList() }, "at most 5 sort" },
        { new ReportQuery { Limit = 0 }, "limit" },
        { new ReportQuery { Limit = 1001 }, "limit" },
        { new ReportQuery { Offset = -1 }, "offset" },
        { new ReportQuery { Offset = 100_001 }, "offset" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_queries_are_rejected_with_a_reason(ReportQuery query, string expectedFragment)
    {
        var act = () => Build(query);

        act.Should().Throw<ReportQueryValidationException>().WithMessage($"*{expectedFragment}*");
    }

    [Fact]
    public void Column_names_never_come_from_the_request()
    {
        var cmd = Build(new ReportQuery { Columns = ["ORDERS"], Filters = [F("CHANNEL", "eq", "x\"; DROP TABLE y; --")] });

        cmd.Sql.Should().NotContain("DROP");
        cmd.Sql.Should().Contain("\"orders\"").And.Contain("\"channel\" = @p0");
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("0,25")]
    public void Decimal_with_comma_is_rejected_not_read_as_thousands(string raw)
    {
        var act = () => Build(new ReportQuery { Filters = [F("revenue_with_vat_czk", "gt", raw)] });

        act.Should().Throw<ReportQueryValidationException>().WithMessage("*not a valid decimal number*");
    }

    [Fact]
    public void Decimal_with_point_parses()
    {
        var cmd = Build(new ReportQuery { Filters = [F("revenue_with_vat_czk", "gt", "1.5")] });

        cmd.Parameters.Single(x => x.ParameterName == "p0").Value.Should().Be(1.5m);
    }

    [Fact]
    public void Null_filters_and_sort_lists_are_treated_as_empty()
    {
        var cmd = Build(new ReportQuery { Filters = null!, Sort = null! });

        cmd.Sql.Should().NotContain("WHERE");
        cmd.Sql.Should().Contain("ORDER BY \"order_month\" DESC");
    }

    [Fact]
    public void Null_sort_direction_defaults_to_asc()
    {
        var cmd = Build(new ReportQuery { Sort = [new ReportSort { Column = "orders", Direction = null! }] });

        cmd.Sql.Should().Contain("ORDER BY \"orders\" ASC NULLS LAST");
    }

    public static TheoryData<ReportQuery, string> NullInputs => new()
    {
        { new ReportQuery { Filters = [new ReportFilter { Column = null!, Op = "eq", Value = "1" }] }, "Unknown column" },
        { new ReportQuery { Filters = [new ReportFilter { Column = "orders", Op = null!, Value = "1" }] }, "Unknown operator" },
        { new ReportQuery { Filters = [new ReportFilter { Column = "channel", Op = "in", Values = new string[] { "a", null! } }] }, "null" },
        { new ReportQuery { Columns = new string[] { "orders", null! } }, "Unknown column" },
        { new ReportQuery { Sort = [new ReportSort { Column = null!, Direction = "asc" }] }, "Unknown column" },
    };

    [Theory]
    [MemberData(nameof(NullInputs))]
    public void Null_inputs_give_a_validation_error_not_a_crash(ReportQuery query, string expectedFragment)
    {
        var act = () => Build(query);

        act.Should().Throw<ReportQueryValidationException>().WithMessage($"*{expectedFragment}*");
    }

    [Fact]
    public void Filter_op_is_trimmed()
    {
        Build(new ReportQuery { Filters = [F("orders", " eq ", "1")] }).Sql.Should().Contain("\"orders\" = @p0");
    }

    [Fact]
    public void Duplicate_columns_are_rejected_case_insensitively()
    {
        var act = () => Build(new ReportQuery { Columns = ["orders", "ORDERS"] });

        act.Should().Throw<ReportQueryValidationException>().WithMessage("*Duplicate column 'orders'*");
    }

    private const string Injection = "orders\"; DROP TABLE x; --";

    [Fact]
    public void Injection_payload_in_columns_is_rejected()
    {
        var act = () => Build(new ReportQuery { Columns = [Injection] });
        act.Should().Throw<ReportQueryValidationException>().WithMessage("*Unknown column*");
    }

    [Fact]
    public void Injection_payload_in_filter_column_is_rejected()
    {
        var act = () => Build(new ReportQuery { Filters = [F(Injection, "eq", "1")] });
        act.Should().Throw<ReportQueryValidationException>().WithMessage("*Unknown column*");
    }

    [Fact]
    public void Injection_payload_in_sort_column_is_rejected()
    {
        var act = () => Build(new ReportQuery { Sort = [new ReportSort { Column = Injection }] });
        act.Should().Throw<ReportQueryValidationException>().WithMessage("*Unknown column*");
    }
}
