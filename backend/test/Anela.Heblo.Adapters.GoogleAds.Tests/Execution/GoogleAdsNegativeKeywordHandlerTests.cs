using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsNegativeKeywordHandlerTests
{
    private static AdAction Campaign(string text = "zdarma", string matchType = "Exact") =>
        ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", text, matchType);

    [Theory]
    [InlineData(AdEntityLevel.Ad)]
    [InlineData(AdEntityLevel.Keyword)]
    [InlineData(AdEntityLevel.NegativeKeyword)]
    public async Task unsupported_levels_fail_and_read_as_missing(AdEntityLevel level)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.AddNegative(level, "111", "zdarma", "Exact");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var state = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
        state.Exists.Should().BeFalse();
        api.Queries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12x")]
    public async Task non_numeric_target_ids_fail_and_read_as_missing(string targetId)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.Campaign, targetId, "zdarma", "Exact");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var state = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
        state.Exists.Should().BeFalse();
        api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task missing_text_and_match_type_keys_fail()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = new AdAction(
            AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, TestSettings.CustomerId,
            AdEntityLevel.Campaign, "111", AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>());

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task missing_match_type_key_alone_fails()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = new AdAction(
            AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, TestSettings.CustomerId,
            AdEntityLevel.Campaign, "111", AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.Text] = "zdarma" });

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task missing_text_key_alone_fails()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = new AdAction(
            AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, TestSettings.CustomerId,
            AdEntityLevel.Campaign, "111", AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.MatchType] = "Exact" });

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task text_of_exactly_80_characters_succeeds()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(Campaign(new string('a', 80)), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        api.Mutations.Should().ContainSingle();
    }

    [Fact]
    public async Task text_of_exactly_10_words_succeeds()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(Campaign("a b c d e f g h i j"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        api.Mutations.Should().ContainSingle();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task whitespace_only_text_fails(string text)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(Campaign(text), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task internal_whitespace_runs_are_collapsed_to_single_spaces_before_sending()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        await executor.ExecuteAsync(Campaign("a\t b\n  c"), CancellationToken.None);

        api.Mutations.Single().Request["operations"]![0]!["create"]!["keyword"]!["text"]!.GetValue<string>()
            .Should().Be("a b c");
    }

    [Fact]
    public async Task an_existing_negative_is_present_for_whitespace_and_case_variants_of_its_text()
    {
        var api = ExecutorHarness.SeededApi();
        api.Negatives.Add(new FakeNegativeKeyword("campaignCriteria", "111", "8000", "A B C", "EXACT"));
        var executor = ExecutorHarness.Create(api);

        var state = await executor.ReadCurrentAsync(Campaign("a\t b\n  c", "Exact"), CancellationToken.None);

        state.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task different_text_with_the_same_match_type_is_absent()
    {
        var api = ExecutorHarness.SeededApi();
        api.Negatives.Add(new FakeNegativeKeyword("campaignCriteria", "111", "8000", "A B C", "EXACT"));
        var executor = ExecutorHarness.Create(api);

        var state = await executor.ReadCurrentAsync(Campaign("a b d", "Exact"), CancellationToken.None);

        state.Should().Be(new AdTargetState(true, AdActionValues.Absent, null));
    }
}
