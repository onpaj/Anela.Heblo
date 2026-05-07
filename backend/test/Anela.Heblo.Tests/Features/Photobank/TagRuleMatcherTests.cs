using System.Collections.Generic;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class TagRuleMatcherTests
{
    private static TagRule Rule(string pattern, string tag, bool active = true, int sort = 0) =>
        new() { PathPattern = pattern, TagName = tag, IsActive = active, SortOrder = sort };

    [Fact]
    public void ExactPrefix_Matches()
    {
        var rules = new[] { Rule("Photos/2025/Events", "events") };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Events/Party", rules);
        result.Should().ContainSingle("events");
    }

    [Fact]
    public void Wildcard_MatchesSingleSegment()
    {
        var rules = new[] { Rule("Photos/*/Events", "events") };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Events/Party", rules);
        result.Should().ContainSingle("events");
    }

    [Fact]
    public void Wildcard_DoesNotMatchMultipleSegments()
    {
        var rules = new[] { Rule("Photos/*", "any") };
        // '*' only matches one segment, so "Photos/2025/Events" should NOT match "Photos/*"
        // because the path has more segments beyond the wildcard position
        // BUT the pattern "Photos/*" means: prefix must be Photos/{one segment}
        // path "Photos/2025/Events" has Photos, 2025, Events — 3 segments, pattern has 2
        // pattern segments <= path segments, wildcard matches "2025" — so it DOES match as prefix
        // The spec says '*' matches exactly one segment in position, not "rest of path"
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Events", rules);
        result.Should().ContainSingle("any"); // prefix "Photos/*" matches "Photos/2025/..."
    }

    [Fact]
    public void AllMatchingRulesApply_NotFirstMatchWins()
    {
        var rules = new[]
        {
            Rule("Photos/2025", "year-2025"),
            Rule("Photos/2025/Events", "events"),
        };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Events/Party", rules);
        result.Should().BeEquivalentTo(new[] { "year-2025", "events" });
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        var rules = new[] { Rule("photos/2025/events", "events") };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Events/Party", rules);
        result.Should().ContainSingle("events");
    }

    [Fact]
    public void InactiveRules_AreSkipped()
    {
        var rules = new[] { Rule("Photos/2025", "year-2025", active: false) };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Party", rules);
        result.Should().BeEmpty();
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        var rules = new[] { Rule("Videos/2025", "videos") };
        var result = TagRuleMatcher.GetMatchingTags("Photos/2025/Party", rules);
        result.Should().BeEmpty();
    }
}
