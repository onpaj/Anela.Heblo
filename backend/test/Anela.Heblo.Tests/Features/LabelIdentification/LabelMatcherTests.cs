using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelMatcherTests
{
    private readonly LabelReferenceIndex _index = new();
    private readonly LabelMatcher _matcher;

    public LabelMatcherTests()
    {
        _matcher = new LabelMatcher(_index, Options.Create(new LabelIdentificationOptions()));
    }

    private string TextFor(string family) =>
        _index.Entries.Single(e => e.Family == family).Normalized;

    [Fact]
    public void Exact_reference_text_auto_confirms_the_right_family()
    {
        var result = _matcher.Match(TextFor("KRE005"));

        result.Decision.Should().Be(LabelMatchDecision.Auto);
        result.Candidates[0].Family.Should().Be("KRE005");
        result.Candidates[0].Score.Should().BeApproximately(100d, 0.01);
    }

    [Fact]
    public void Two_size_family_returns_both_variant_codes()
    {
        var result = _matcher.Match(TextFor("KRE005"));

        result.Candidates[0].Codes.Should().BeEquivalentTo(new[] { "KRE005015", "KRE005030" });
    }

    [Fact]
    public void Single_size_family_returns_one_code()
    {
        var result = _matcher.Match(TextFor("PEE002"));

        result.Candidates[0].Family.Should().Be("PEE002");
        result.Candidates[0].Codes.Should().ContainSingle();
    }

    [Fact]
    public void The_closest_confusable_pair_still_clears_the_auto_margin()
    {
        // MAS007 vs KRE005 score 87.7 against each other — the tightest pair in the
        // corpus. A perfect KRE005 match must still win by more than the margin.
        var result = _matcher.Match(TextFor("KRE005"));

        result.Candidates[0].Family.Should().Be("KRE005");
        result.Candidates[1].Family.Should().Be("MAS007");
        (result.Candidates[0].Score - result.Candidates[1].Score).Should().BeGreaterThan(5d);
        result.Decision.Should().Be(LabelMatchDecision.Auto);
    }

    [Fact]
    public void Survives_appended_ghost_text_from_neighbouring_stickers()
    {
        var text = TextFor("MAS001");
        var withGhost = text + ", " + string.Join(", ", text.Split(',').Take(5));

        var result = _matcher.Match(withGhost);

        result.Candidates[0].Family.Should().Be("MAS001");
        result.Decision.Should().Be(LabelMatchDecision.Auto);
    }

    [Fact]
    public void Survives_reordered_ingredients()
    {
        var parts = TextFor("MAS001").Split(',', StringSplitOptions.TrimEntries).ToList();
        parts.Reverse();

        var result = _matcher.Match(string.Join(", ", parts));

        result.Candidates[0].Family.Should().Be("MAS001");
    }

    [Fact]
    public void Survives_dropped_characters_from_imperfect_ocr()
    {
        var text = TextFor("MAS001");
        var mangled = new string(text.Where((_, i) => i % 17 != 0).ToArray());

        var result = _matcher.Match(mangled);

        result.Candidates[0].Family.Should().Be("MAS001");
    }

    [Fact]
    public void Garbage_input_is_low_confidence_and_never_a_confident_code()
    {
        var result = _matcher.Match("qqq www zzz nothing like an ingredient list at all");

        result.Decision.Should().Be(LabelMatchDecision.Low);
    }

    [Fact]
    public void Returns_at_most_three_candidates_ranked_descending()
    {
        var result = _matcher.Match(TextFor("MAS001"));

        result.Candidates.Should().HaveCount(3);
        result.Candidates.Should().BeInDescendingOrder(c => c.Score);
    }

    [Fact]
    public void Blank_input_is_low_confidence_with_no_candidates()
    {
        var result = _matcher.Match("   ");

        result.Decision.Should().Be(LabelMatchDecision.Low);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void A_narrow_lead_falls_back_to_choose_rather_than_auto_confirming()
    {
        var options = Options.Create(new LabelIdentificationOptions { AutoConfirmMargin = 99 });
        var matcher = new LabelMatcher(_index, options);

        var result = matcher.Match(TextFor("KRE005"));

        result.Decision.Should().Be(LabelMatchDecision.Choose);
    }
}
