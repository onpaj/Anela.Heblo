using System.Collections.Generic;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class TagRuleMatcherTests
{
    private static TagRule Rule(string pattern, string tagName, bool isActive = true) =>
        new() { Id = 1, PathPattern = pattern, TagName = tagName, IsActive = isActive, SortOrder = 0 };

    [Fact]
    public void GetMatchingTags_FindsTagInsideFolderSegment()
    {
        var rule = Rule("peťa", "peta");
        var path = "Grafika_interní/PROFI_FOCENI/_Vánoce + Advent/2024_Háta/garance dodání_Andy, Peťa";
        var fileName = "dsc-6411.jpg";

        var result = TagRuleMatcher.GetMatchingTags(path, fileName, new[] { rule });

        result.Should().Contain("peta");
    }

    [Fact]
    public void GetMatchingTags_MatchesFileName_WhenPatternAppearsOnlyInFileName()
    {
        var rule = Rule("andy", "andy");
        var result = TagRuleMatcher.GetMatchingTags("marketing/2024", "andy_portrait.jpg", new[] { rule });
        result.Should().Contain("andy");
    }

    [Fact]
    public void GetMatchingTags_TranslatedLegacyWildcard_StillMatches()
    {
        var rule = Rule(@"^people/[^/]+/2024(/|$)", "team2024");
        var result = TagRuleMatcher.GetMatchingTags("people/foo/2024", "photo.jpg", new[] { rule });
        result.Should().Contain("team2024");
    }

    [Fact]
    public void GetMatchingTags_TranslatedLegacyWildcard_DoesNotMatchMultipleSegments()
    {
        var rule = Rule(@"^people/[^/]+/2024(/|$)", "team2024");
        var result = TagRuleMatcher.GetMatchingTags("people/foo/bar/2024", "photo.jpg", new[] { rule });
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMatchingTags_CaseInsensitive()
    {
        var rule = Rule("profi", "profi");
        var result = TagRuleMatcher.GetMatchingTags("Grafika/PROFI_FOCENI", "img.jpg", new[] { rule });
        result.Should().Contain("profi");
    }

    [Fact]
    public void GetMatchingTags_DiacriticSensitive_PatternWithHacek_DoesNotMatchPlain()
    {
        var rule = Rule("peťa", "peta");
        var result = TagRuleMatcher.GetMatchingTags("marketing/peta_photos", "img.jpg", new[] { rule });
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMatchingTags_DiacriticSensitive_PatternPlain_DoesNotMatchHacek()
    {
        var rule = Rule("peta", "peta_plain");
        var result = TagRuleMatcher.GetMatchingTags("marketing/Peťa", "img.jpg", new[] { rule });
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMatchingTags_SkipsInactiveRules()
    {
        var rule = Rule("photos", "photos", isActive: false);
        var result = TagRuleMatcher.GetMatchingTags("photos/2024", "img.jpg", new[] { rule });
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMatchingTags_ReturnsDistinctTagNames()
    {
        var rules = new[]
        {
            Rule("photos", "products"),
            Rule("2024", "products"),
        };
        var result = TagRuleMatcher.GetMatchingTags("photos/2024", "img.jpg", rules);
        result.Should().ContainSingle(t => t == "products");
    }

    [Fact]
    public void GetMatchingTags_EmptyFolderPathAndFileName_ReturnsEmpty()
    {
        var rule = Rule("photos", "photos");
        var result = TagRuleMatcher.GetMatchingTags("", "", new[] { rule });
        result.Should().BeEmpty();
    }
}
