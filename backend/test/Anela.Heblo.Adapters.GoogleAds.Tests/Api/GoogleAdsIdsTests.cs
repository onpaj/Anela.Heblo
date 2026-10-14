using Anela.Heblo.Adapters.GoogleAds.Api;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsIdsTests
{
    [Theory]
    [InlineData("111")]
    [InlineData("12345678901234567890")]
    public void accepts_plain_digits(string value) =>
        GoogleAdsIds.IsNumericId(value).Should().BeTrue();

    [Theory]
    [InlineData("111\n")]
    [InlineData("\n111")]
    [InlineData("11 1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("123456789012345678901")]
    public void rejects_anything_but_plain_digits(string? value) =>
        GoogleAdsIds.IsNumericId(value).Should().BeFalse();

    [Fact]
    public void rejects_a_composite_id_with_a_trailing_newline() =>
        GoogleAdsIds.TryParseComposite("221~441\n", out _, out _).Should().BeFalse();
}
