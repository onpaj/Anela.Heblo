using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdActionExecutorTests
{
    private const string Account = "123";

    private static AdAction PauseAd(AdPlatform platform = AdPlatform.GoogleAds) =>
        new(AdActionType.PauseAd, platform, Account, AdEntityLevel.Ad, "ad-1",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdAction AddNegative(string text = "zdarma") =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.Campaign, "campaign-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = text,
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
            });

    [Fact]
    public async Task RejectNextExecute_returns_Failed_once_and_then_executes_normally()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1").RejectNextExecute("POLICY_VIOLATION");

        var rejected = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);
        var executed = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);

        rejected.Outcome.Should().Be(AdExecutionOutcome.Failed);
        rejected.Error.Should().Be("POLICY_VIOLATION");
        executed.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        executor.ExecutedActions.Should().ContainSingle();
    }

    [Fact]
    public async Task FailWith_makes_calls_throw_like_a_transport_failure()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1").FailWith(new HttpRequestException("timeout"));

        var act = () => executor.ExecuteAsync(PauseAd(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        executor.ExecutedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_action_for_another_platform_is_rejected_as_a_programming_error()
    {
        var executor = new FakeAdActionExecutor(AdPlatform.GoogleAds).SeedAd(Account, "ad-1");

        var act = () => executor.ExecuteAsync(PauseAd(AdPlatform.Sklik), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_unsupported_action_type_returns_Failed()
    {
        var executor = new FakeAdActionExecutor(AdPlatform.GoogleAds, AdActionType.PauseAd)
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var result = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("not supported");
    }

    [Fact]
    public async Task Reverting_an_unsupported_action_type_returns_Failed()
    {
        var executor = new FakeAdActionExecutor(AdPlatform.GoogleAds, AdActionType.PauseAd)
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");
        var original = new AdExecutionResult(
            AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, "fake-negative-1", null, null);

        var result = await executor.RevertAsync(AddNegative(), original, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("not supported");
        executor.RevertedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reverting_a_failed_execution_returns_Failed_and_changes_nothing()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1");
        var failed = new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, null, "boom");

        var result = await executor.RevertAsync(PauseAd(), failed, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        (await executor.ReadCurrentAsync(PauseAd(), CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Enabled);
        executor.RevertedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Adding_the_same_negative_keyword_twice_fails_the_second_time()
    {
        var executor = new FakeAdActionExecutor().SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var first = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);
        var second = await executor.ExecuteAsync(AddNegative(), CancellationToken.None);

        first.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        first.PlatformResourceId.Should().Be("fake-negative-1");
        second.Outcome.Should().Be(AdExecutionOutcome.Failed);
    }

    [Fact]
    public async Task A_negative_keyword_without_text_in_the_payload_returns_Failed()
    {
        var executor = new FakeAdActionExecutor().SeedNegativeKeywordTarget(Account, AdEntityLevel.Campaign, "campaign-1");

        var result = await executor.ExecuteAsync(AddNegative(text: " "), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
    }

    [Fact]
    public async Task Successful_execute_and_revert_are_recorded()
    {
        var executor = new FakeAdActionExecutor().SeedAd(Account, "ad-1");

        var executed = await executor.ExecuteAsync(PauseAd(), CancellationToken.None);
        await executor.RevertAsync(PauseAd(), executed, CancellationToken.None);

        executor.ExecutedActions.Should().ContainSingle().Which.TargetExternalId.Should().Be("ad-1");
        executor.RevertedActions.Should().ContainSingle();
    }
}
