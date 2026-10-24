using Anela.Heblo.Adapters.GoogleAds.Execution;
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

    private static AdAction AdGroup(string text = "zdarma", string matchType = "Broad") =>
        ExecutorHarness.AddNegative(AdEntityLevel.AdGroup, "221", text, matchType);

    private static AdExecutionResult Original(AdExecutionOutcome outcome, string? resourceName) =>
        new(outcome, AdActionValues.Absent, AdActionValues.Present, resourceName, "{}", null);

    [Fact]
    public async Task revert_of_a_failed_original_fails_even_with_a_resource_name()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = Original(AdExecutionOutcome.Failed, "customers/1234567890/campaignCriteria/111~9000");

        var reverted = await executor.RevertAsync(Campaign(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task revert_of_a_succeeded_original_without_an_id_fails()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var reverted = await executor.RevertAsync(Campaign(), Original(AdExecutionOutcome.Succeeded, null), CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task revert_with_an_invalid_payload_fails_even_for_a_valid_original()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = Original(AdExecutionOutcome.Succeeded, "customers/1234567890/campaignCriteria/111~9000");

        var reverted = await executor.RevertAsync(Campaign(text: ""), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("customers/1234567890/campaignCriteria/221~9000")]
    [InlineData("customers/1234567890/adGroupCriteria/222~9000")]
    public async Task ad_group_revert_refuses_a_campaign_criterion_or_the_wrong_parent(string resourceName)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var reverted = await executor.RevertAsync(
            AdGroup(), Original(AdExecutionOutcome.Succeeded, resourceName), CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task ad_group_200_naming_no_criterion_is_failed_so_it_is_checked_by_hand()
    {
        var api = ExecutorHarness.SeededApi();
        api.SucceedNextMutateWithoutResourceName();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(AdGroup(), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.PlatformResourceId.Should().BeNull();
        result.Error.Should().Contain("verify in the account");
    }

    [Theory]
    [InlineData(AdEntityLevel.Campaign, "111", "campaignCriteria")]
    [InlineData(AdEntityLevel.AdGroup, "221", "adGroupCriteria")]
    public async Task validate_only_execute_sends_one_validate_only_mutation_to_the_right_service(
        AdEntityLevel level, string target, string service)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: true);

        var result = await executor.ExecuteAsync(
            ExecutorHarness.AddNegative(level, target, "zdarma", "Exact"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        result.PlatformResourceId.Should().BeNull();
        var mutation = api.Mutations.Should().ContainSingle().Subject;
        mutation.Service.Should().Be(service);
        mutation.Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.Negatives.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AdEntityLevel.Campaign, "111", "campaignCriteria")]
    [InlineData(AdEntityLevel.AdGroup, "221", "adGroupCriteria")]
    public async Task validate_only_revert_sends_a_validate_only_remove_and_keeps_the_criterion(
        AdEntityLevel level, string target, string service)
    {
        var api = ExecutorHarness.SeededApi();
        var action = ExecutorHarness.AddNegative(level, target, "zdarma", "Exact");
        var original = await ExecutorHarness.Create(api).ExecuteAsync(action, CancellationToken.None);
        var validating = ExecutorHarness.Create(api, validateOnly: true);

        var reverted = await validating.RevertAsync(action, original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        reverted.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        api.Mutations.Should().HaveCount(2);
        api.Mutations.Last().Service.Should().Be(service);
        api.Mutations.Last().Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.Mutations.Last().Request["operations"]![0]!["remove"]!.GetValue<string>().Should().Be(original.PlatformResourceId);
        api.Negatives.Should().ContainSingle();
    }
}
