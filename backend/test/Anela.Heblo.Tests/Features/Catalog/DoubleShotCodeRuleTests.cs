using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class DoubleShotCodeRuleTests
{
    [Theory]
    [InlineData("SA015030V", true)]
    [InlineData("SA014005V", true)]
    [InlineData("SA017100V", true)]
    [InlineData("BAL0001V", false)]
    [InlineData("SA015030S", false)]
    [InlineData("SA001", false)]
    [InlineData("SA015030", false)]
    [InlineData("sa015030v", false)]
    [InlineData("SAV", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVariantCode_MatchesOnlySaDigitsV(string? code, bool expected)
    {
        DoubleShotCodeRule.IsVariantCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("SA015030V", "SA015030S")]
    [InlineData("SA014100V", "SA014100S")]
    public void TryGetSetCode_SwapsTrailingVForS(string variant, string expected)
    {
        DoubleShotCodeRule.TryGetSetCode(variant, out var setCode).Should().BeTrue();
        setCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("BAL0001V")]
    [InlineData("SA015030S")]
    [InlineData("SA015030")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetSetCode_ReturnsFalse_ForNonVariantCodes(string? code)
    {
        DoubleShotCodeRule.TryGetSetCode(code, out var setCode).Should().BeFalse();
        setCode.Should().BeEmpty();
    }

    [Fact]
    public void ToVariantCode_SwapsTrailingSForV()
    {
        DoubleShotCodeRule.ToVariantCode("SA016005S").Should().Be("SA016005V");
    }

    [Theory]
    [InlineData("SA016005V")]
    [InlineData("SA016005")]
    [InlineData("SA001")]
    [InlineData("")]
    public void ToVariantCode_Throws_ForNonSetCodes(string code)
    {
        var act = () => DoubleShotCodeRule.ToVariantCode(code);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RoundTrip_VariantToSetAndBack()
    {
        DoubleShotCodeRule.TryGetSetCode("SA015005V", out var setCode);

        DoubleShotCodeRule.ToVariantCode(setCode).Should().Be("SA015005V");
    }
}
