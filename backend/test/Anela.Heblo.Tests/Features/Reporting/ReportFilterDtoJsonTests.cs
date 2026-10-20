using System.Text.Json;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using FluentAssertions;
using ModelContextProtocol;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportFilterDtoJsonTests
{
    public static IEnumerable<object[]> Options() =>
    [
        [new JsonSerializerOptions(JsonSerializerDefaults.Web)],
        [McpJsonUtilities.DefaultOptions],
    ];

    [Theory]
    [MemberData(nameof(Options))]
    public void Number_value_is_read_as_its_raw_text(JsonSerializerOptions options)
    {
        var f = JsonSerializer.Deserialize<ReportFilterDto>("""{"column":"orders","op":"gt","value":100}""", options)!;

        f.Value.Should().Be("100");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Mixed_values_are_read_as_strings(JsonSerializerOptions options)
    {
        var f = JsonSerializer.Deserialize<ReportFilterDto>(
            """{"column":"order_month","op":"in","values":["2026-01",2026.5,true]}""", options)!;

        f.Values.Should().Equal("2026-01", "2026.5", "true");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Boolean_value_is_read_as_lowercase_text(JsonSerializerOptions options)
    {
        var f = JsonSerializer.Deserialize<ReportFilterDto>("""{"column":"x","op":"isNull","value":true}""", options)!;

        f.Value.Should().Be("true");
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void Null_value_and_values_stay_null(JsonSerializerOptions options)
    {
        var f = JsonSerializer.Deserialize<ReportFilterDto>("""{"column":"x","op":"isNull","value":null,"values":null}""", options)!;

        f.Value.Should().BeNull();
        f.Values.Should().BeNull();
    }

    [Fact]
    public void Object_value_is_rejected()
    {
        var act = () => JsonSerializer.Deserialize<ReportFilterDto>("""{"column":"x","op":"eq","value":{}}""", McpJsonUtilities.DefaultOptions);

        act.Should().Throw<JsonException>();
    }
}
