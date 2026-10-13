using System.Text.Json;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsJsonTests
{
    private static readonly JsonElement Row = JsonDocument.Parse("""
        {"campaign":{"id":"111","name":"Brand"},
         "metrics":{"impressions":"1200","costMicros":"432100000","conversions":4.5,"conversionsValue":1.5E3},
         "customer":{"manager":true}}
        """).RootElement;

    [Fact]
    public void reads_int64_strings_doubles_and_micros()
    {
        GoogleAdsJson.Int64(Row, "metrics", "impressions").Should().Be(1200);
        GoogleAdsJson.Micros(Row, "metrics", "costMicros").Should().Be(432.1m);
        GoogleAdsJson.Decimal(Row, "metrics", "conversions").Should().Be(4.5m);
        GoogleAdsJson.Decimal(Row, "metrics", "conversionsValue").Should().Be(1500m);
    }

    [Fact]
    public void treats_omitted_numbers_as_zero_and_omitted_booleans_as_false()
    {
        GoogleAdsJson.Int64(Row, "metrics", "clicks").Should().Be(0);
        GoogleAdsJson.Micros(Row, "metrics", "missingMicros").Should().Be(0m);
        GoogleAdsJson.Bool(Row, "campaign", "manager").Should().BeFalse();
        GoogleAdsJson.Bool(Row, "customer", "manager").Should().BeTrue();
    }

    [Fact]
    public void required_string_names_the_missing_path()
    {
        var act = () => GoogleAdsJson.RequiredString(Row, "adGroup", "id");

        act.Should().Throw<InvalidOperationException>().WithMessage("*adGroup.id*");
    }
}
