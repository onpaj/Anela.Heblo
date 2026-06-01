using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class TagRulePatternTranslatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Translate_ThrowsArgumentException_ForNullOrWhiteSpace(string pattern)
    {
        var act = () => TagRulePatternTranslator.Translate(pattern);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Photos/*", "OtherFolder/x/photo.jpg")]
    [InlineData("Photos/*/2024", "Photos/foo/bar/2024/img.jpg")]
    public void Translate_ProducesRegexThatDoesNotMatchOtherPaths(string legacyPattern, string path)
    {
        var regex = TagRulePatternTranslator.Translate(legacyPattern);
        Regex.IsMatch(path, regex, RegexOptions.IgnoreCase).Should().BeFalse();
    }

    [Theory]
    [InlineData("Photos", @"^Photos(/|$)")]
    [InlineData("Photos/Products", @"^Photos/Products(/|$)")]
    [InlineData("Photos/*", @"^Photos/[^/]+(/|$)")]
    [InlineData("Photos/*/2024", @"^Photos/[^/]+/2024(/|$)")]
    [InlineData("*", @"^[^/]+(/|$)")]
    public void Translate_ConvertsLegacyGlobToRegex(string pattern, string expected)
    {
        var result = TagRulePatternTranslator.Translate(pattern);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Photos", "Photos/Products/photo.jpg")]
    [InlineData("Photos/*", "Photos/Anything/photo.jpg")]
    [InlineData("Photos/*/2024", "Photos/Team/2024/img.jpg")]
    public void Translate_ProducesRegexThatMatchesSamePaths(string legacyPattern, string path)
    {
        var regex = TagRulePatternTranslator.Translate(legacyPattern);
        Regex.IsMatch(path, regex, RegexOptions.IgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Translate_EscapesRegexSpecialChars_InLiteralSegments()
    {
        var regex = TagRulePatternTranslator.Translate("Anela.Heblo/Photos");
        Regex.IsMatch("AnelaXHeblo/Photos/img.jpg", regex, RegexOptions.IgnoreCase).Should().BeFalse();
        Regex.IsMatch("Anela.Heblo/Photos/img.jpg", regex, RegexOptions.IgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void Translate_IdempotentCheck_SkipsAlreadyMigratedPatterns()
    {
        var alreadyRegex = @"^Photos/[^/]+(/|$)";
        var result = TagRulePatternTranslator.Translate(alreadyRegex);
        result.Should().Be(alreadyRegex);
    }
}
