using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class AdActionExecutorContractSelfTests
{
    private const string Account = "123";

    [Fact]
    public async Task Contract_fails_an_executor_that_throws_on_a_missing_target()
    {
        var suite = new Suite(() => new ThrowingOnMissingTarget(NewFake()));

        var act = () => suite.Execute_on_a_missing_target_returns_Failed_instead_of_throwing();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_whose_revert_does_not_restore_the_ad()
    {
        var suite = new Suite(() => new RevertDoesNothing(NewFake()));

        var act = () => suite.PauseAd_Revert_restores_the_value_ReadCurrent_reported_before_Execute();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_does_not_return_a_resource_id_for_a_new_negative_keyword()
    {
        var suite = new Suite(() => new NoResourceId(NewFake()));

        var act = () => suite.AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id();

        await act.Should().ThrowAsync<Exception>();
    }

    private static FakeAdActionExecutor NewFake() =>
        new FakeAdActionExecutor(AdPlatform.GoogleAds)
            .SeedAd(Account, "ad-1")
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.AdGroup, "adgroup-1");

    private sealed class Suite(Func<IAdActionExecutor> create) : AdActionExecutorContractTests
    {
        protected override IAdActionExecutor CreateExecutor() => create();

        protected override AdAction SamplePauseAd() =>
            new(AdActionType.PauseAd, AdPlatform.GoogleAds, Account, AdEntityLevel.Ad, "ad-1",
                AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

        protected override AdAction? SampleAddNegativeKeyword() =>
            new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.AdGroup, "adgroup-1",
                AdActionValues.Absent, AdActionValues.Present,
                new Dictionary<string, string>
                {
                    [AdActionPayloadKeys.Text] = "zdarma",
                    [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
                });
    }

    private abstract class Decorator(IAdActionExecutor inner) : IAdActionExecutor
    {
        public AdPlatform Platform => inner.Platform;
        public IReadOnlySet<AdActionType> SupportedActions => inner.SupportedActions;
        public virtual Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct) => inner.ReadCurrentAsync(action, ct);
        public virtual Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) => inner.ExecuteAsync(action, ct);
        public virtual Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct) =>
            inner.RevertAsync(action, original, ct);
    }

    private sealed class ThrowingOnMissingTarget(IAdActionExecutor inner) : Decorator(inner)
    {
        public override Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) =>
            action.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? throw new InvalidOperationException("404 from the platform")
                : base.ExecuteAsync(action, ct);
    }

    private sealed class RevertDoesNothing(IAdActionExecutor inner) : Decorator(inner)
    {
        public override Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct) =>
            Task.FromResult(new AdExecutionResult(AdExecutionOutcome.Succeeded, null, null, null, null, null));
    }

    private sealed class NoResourceId(IAdActionExecutor inner) : Decorator(inner)
    {
        public override async Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) =>
            (await base.ExecuteAsync(action, ct)) with { PlatformResourceId = null };
    }
}
