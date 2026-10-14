using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsAddNegativeKeywordTests
{
    private static AdAction CampaignNegative(string text = "zdarma", string matchType = "Exact") =>
        ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", text, matchType);

    [Fact]
    public async Task is_absent_then_present_after_execute()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var before = await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None);
        var result = await executor.ExecuteAsync(CampaignNegative(), CancellationToken.None);
        var after = await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None);

        before.Should().Be(new AdTargetState(true, AdActionValues.Absent, null));
        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        result.PlatformResourceId.Should().Be("customers/1234567890/campaignCriteria/111~9000");
        (result.BeforeValue, result.AfterValue).Should().Be((AdActionValues.Absent, AdActionValues.Present));
        after.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task sends_a_negative_keyword_criterion_for_the_campaign()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        await executor.ExecuteAsync(CampaignNegative("  levná   kosmetika ", "Phrase"), CancellationToken.None);

        var (service, request) = api.Mutations.Single();
        service.Should().Be("campaignCriteria");
        var create = request["operations"]![0]!["create"]!;
        create["campaign"]!.GetValue<string>().Should().Be("customers/1234567890/campaigns/111");
        create["negative"]!.GetValue<bool>().Should().BeTrue();
        create["keyword"]!["text"]!.GetValue<string>().Should().Be("levná kosmetika");
        create["keyword"]!["matchType"]!.GetValue<string>().Should().Be("PHRASE");
    }

    [Fact]
    public async Task uses_ad_group_criteria_for_an_ad_group_target()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.AdGroup, "221", "zdarma", "Broad");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        api.Mutations.Single().Service.Should().Be("adGroupCriteria");
        api.Mutations.Single().Request["operations"]![0]!["create"]!["adGroup"]!.GetValue<string>()
            .Should().Be("customers/1234567890/adGroups/221");
        result.PlatformResourceId.Should().StartWith("customers/1234567890/adGroupCriteria/221~");
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task matches_an_existing_negative_ignoring_case_and_extra_spaces_but_not_another_match_type()
    {
        var api = ExecutorHarness.SeededApi();
        api.Negatives.Add(new FakeNegativeKeyword("campaignCriteria", "111", "8000", "Zdarma Doprava", "EXACT"));
        var executor = ExecutorHarness.Create(api);

        var same = await executor.ReadCurrentAsync(CampaignNegative(" zdarma  doprava ", "Exact"), CancellationToken.None);
        var otherType = await executor.ReadCurrentAsync(CampaignNegative("zdarma doprava", "Phrase"), CancellationToken.None);

        same.CurrentValue.Should().Be(AdActionValues.Present);
        otherType.CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task never_puts_keyword_text_into_a_query()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        const string hostile = "x' OR campaign.id = 1 --";
        var action = CampaignNegative(hostile, "Exact");

        await executor.ReadCurrentAsync(action, CancellationToken.None);
        await executor.ExecuteAsync(action, CancellationToken.None);

        api.Queries.Should().NotBeEmpty().And.OnlyContain(q => !q.Gaql.Contains("OR campaign.id = 1"));
        api.Mutations.Single().Request["operations"]![0]!["create"]!["keyword"]!["text"]!.GetValue<string>()
            .Should().Be(hostile);
    }

    [Fact]
    public async Task revert_removes_the_created_criterion()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = await executor.ExecuteAsync(CampaignNegative(), CancellationToken.None);

        var reverted = await executor.RevertAsync(CampaignNegative(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        api.Mutations.Last().Request["operations"]![0]!["remove"]!.GetValue<string>().Should().Be(original.PlatformResourceId);
        (await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Theory]
    [InlineData("customers/5555555555/campaignCriteria/111~9000")]
    [InlineData("customers/1234567890/campaignBudgets/901")]
    [InlineData(null)]
    public async Task revert_refuses_a_resource_that_is_not_our_negative_criterion(string? resourceName)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, resourceName, "{}", null);

        var reverted = await executor.RevertAsync(CampaignNegative(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("customers/1234567890/adGroupCriteria/111~9000")]
    [InlineData("customers/1234567890/campaignCriteria/222~9000")]
    [InlineData("customers/1234567890/campaignCriteria/111~9000\n")]
    [InlineData("customers/1234567890/campaignCriteria/111~abc")]
    public async Task revert_refuses_a_criterion_that_does_not_belong_to_the_reverted_action(string resourceName)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, resourceName, "{}", null);

        var reverted = await executor.RevertAsync(CampaignNegative(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task ad_group_negative_round_trip_removes_the_criterion()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.AdGroup, "221", "zdarma", "Broad");
        var original = await executor.ExecuteAsync(action, CancellationToken.None);

        var reverted = await executor.RevertAsync(action, original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        api.Mutations.Last().Service.Should().Be("adGroupCriteria");
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Theory]
    [InlineData("", "Exact")]
    [InlineData("zdarma", "")]
    [InlineData("zdarma", "1")]
    [InlineData("zdarma", "NEAR_EXACT")]
    [InlineData("a b c d e f g h i j k", "Exact")]
    public async Task invalid_payloads_fail_without_calling_google(string text, string matchType)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(CampaignNegative(text, matchType), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task text_longer_than_80_characters_fails()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(CampaignNegative(new string('a', 81)), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task an_unknown_campaign_does_not_exist()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(
            ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "999", "zdarma", "Exact"), CancellationToken.None);

        state.Should().Be(new AdTargetState(false, null, null));
    }
}
