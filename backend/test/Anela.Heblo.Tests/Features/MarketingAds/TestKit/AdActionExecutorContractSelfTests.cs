using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;
using Xunit.Sdk;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class AdActionExecutorContractSelfTests
{
    private const string Account = "123";

    [Fact]
    public async Task Contract_fails_an_executor_that_throws_on_a_missing_target()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: (inner, a, ct) =>
            a.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? throw new InvalidOperationException("404 from the platform")
                : inner.ExecuteAsync(a, ct)));

        var act = () => suite.Execute_on_a_missing_target_returns_Failed_instead_of_throwing();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_throws_on_a_missing_negative_keyword_target()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: (inner, a, ct) =>
            a.Type == AdActionType.AddNegativeKeyword
            && a.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? throw new InvalidOperationException("NOT_FOUND from the platform")
                : inner.ExecuteAsync(a, ct)));

        var act = () => suite.AddNegativeKeyword_Execute_on_a_missing_target_returns_Failed_instead_of_throwing();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_reports_a_missing_negative_keyword_target_as_existing()
    {
        var suite = new Suite(() => new Mutant(NewFake(), read: async (inner, a, ct) =>
            a.Type == AdActionType.AddNegativeKeyword
            && a.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? new AdTargetState(true, AdActionValues.Absent, null)
                : await inner.ReadCurrentAsync(a, ct)));

        await Catches(() => suite.AddNegativeKeyword_ReadCurrent_on_a_missing_target_reports_that_it_does_not_exist());
    }

    [Fact]
    public async Task Contract_fails_an_executor_whose_pause_revert_does_not_restore_the_ad()
    {
        var suite = new Suite(() => new Mutant(NewFake(), revert: (_, _, _, _) => Task.FromResult(Succeeded(null, null))));

        await Catches(() => suite.PauseAd_Revert_restores_the_value_ReadCurrent_reported_before_Execute());
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_does_not_return_a_resource_id_for_a_new_negative_keyword()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: async (inner, a, ct) =>
            (await inner.ExecuteAsync(a, ct)) with { PlatformResourceId = null }));

        await Catches(() => suite.AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id());
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_reports_a_missing_target_as_existing()
    {
        var suite = new Suite(() => new Mutant(NewFake(), read: async (inner, a, ct) =>
            a.TargetExternalId == AdActionExecutorContractTests.MissingTargetExternalId
                ? new AdTargetState(true, AdActionValues.Enabled, null)
                : await inner.ReadCurrentAsync(a, ct)));

        await Catches(() => suite.ReadCurrent_on_a_missing_target_reports_that_it_does_not_exist());
    }

    [Fact]
    public async Task Contract_fails_a_PauseAd_Execute_that_reports_success_without_pausing()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: (_, _, _) =>
            Task.FromResult(Succeeded(AdActionValues.Enabled, AdActionValues.Paused, "ad-1"))));

        await Catches(() => suite.PauseAd_Execute_pauses_the_ad_and_reports_the_before_and_after_values());
    }

    [Fact]
    public async Task Contract_fails_an_AddNegativeKeyword_Execute_that_reports_success_without_creating()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: (_, _, _) =>
            Task.FromResult(Succeeded(AdActionValues.Absent, AdActionValues.Present, "fake-negative-1"))));

        await Catches(() => suite.AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id());
    }

    [Fact]
    public async Task Contract_fails_a_PauseAd_Execute_that_reports_a_wrong_BeforeValue()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: async (inner, a, ct) =>
            (await inner.ExecuteAsync(a, ct)) with { BeforeValue = AdActionValues.Paused }));

        await Catches(() => suite.PauseAd_Execute_pauses_the_ad_and_reports_the_before_and_after_values());
    }

    [Fact]
    public async Task Contract_fails_an_AddNegativeKeyword_Execute_that_reports_a_wrong_AfterValue()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: async (inner, a, ct) =>
            (await inner.ExecuteAsync(a, ct)) with { AfterValue = AdActionValues.Absent }));

        await Catches(() => suite.AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id());
    }

    [Fact]
    public async Task Contract_fails_an_AddNegativeKeyword_revert_that_does_not_remove_the_criterion()
    {
        var suite = new Suite(() => new Mutant(NewFake(), revert: (_, _, _, _) =>
            Task.FromResult(Succeeded(AdActionValues.Present, AdActionValues.Absent))));

        await Catches(() => suite.AddNegativeKeyword_Revert_restores_the_value_ReadCurrent_reported_before_Execute());
    }

    [Fact]
    public async Task Contract_fails_a_negative_keyword_read_that_reports_Present_before_Execute()
    {
        var suite = new Suite(() => new Mutant(NewFake(), read: async (inner, a, ct) =>
            a.Type == AdActionType.AddNegativeKeyword
                ? new AdTargetState(true, AdActionValues.Present, null)
                : await inner.ReadCurrentAsync(a, ct)));

        await Catches(() => suite.AddNegativeKeyword_ReadCurrent_reports_Absent_before_Execute());
    }

    [Fact]
    public async Task Contract_fails_a_PauseAd_read_that_does_not_report_an_enabled_ad()
    {
        var suite = new Suite(() => new Mutant(NewFake(), read: async (inner, a, ct) =>
            a.Type == AdActionType.PauseAd
                ? (await inner.ReadCurrentAsync(a, ct)) with { CurrentValue = AdActionValues.Paused }
                : await inner.ReadCurrentAsync(a, ct)));

        await Catches(() => suite.PauseAd_ReadCurrent_reports_an_existing_enabled_ad_before_Execute());
    }

    [Fact]
    public async Task Contract_fails_an_executor_that_compares_OldValue_itself()
    {
        var suite = new Suite(() => new Mutant(NewFake(), execute: async (inner, a, ct) =>
            (await inner.ReadCurrentAsync(a, ct)).CurrentValue != a.OldValue
                ? new AdExecutionResult(AdExecutionOutcome.StaleState, null, null, null, null, "stale")
                : await inner.ExecuteAsync(a, ct)));

        await Catches(() => suite.Execute_does_not_compare_OldValue_itself_the_core_does_the_stale_check());
    }

    [Fact]
    public async Task Contract_fails_an_executor_whose_SupportedActions_omit_a_sampled_action()
    {
        var suite = new Suite(() => new Mutant(NewFake(), supported: new HashSet<AdActionType> { AdActionType.PauseAd }));

        await Catches(() =>
        {
            suite.Platform_is_defined_and_SupportedActions_match_the_samples();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Contract_fails_a_PauseAd_sample_that_breaks_the_conventions()
    {
        var suite = new Suite(() => NewFake(),
            pauseSample: Pause() with { NewValue = AdActionValues.Enabled });

        await Catches(() =>
        {
            suite.SamplePauseAd_follows_the_PauseAd_conventions();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Contract_fails_a_negative_keyword_sample_with_an_unknown_match_type()
    {
        var sample = Negative();
        var suite = new Suite(() => NewFake(),
            negativeSample: sample with
            {
                Payload = new Dictionary<string, string>
                {
                    [AdActionPayloadKeys.Text] = "zdarma",
                    [AdActionPayloadKeys.MatchType] = "Fuzzy",
                },
            });

        await Catches(() =>
        {
            suite.SampleAddNegativeKeyword_follows_the_AddNegativeKeyword_conventions();
            return Task.CompletedTask;
        });
    }

    /// <summary>A broken executor must make the inherited fact fail with an assertion, not just any exception.</summary>
    private static async Task Catches(Func<Task> fact)
    {
        var act = () => fact();
        await act.Should().ThrowAsync<XunitException>();
    }

    private static AdAction Pause() =>
        new(AdActionType.PauseAd, AdPlatform.GoogleAds, Account, AdEntityLevel.Ad, "ad-1",
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdAction Negative() =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account, AdEntityLevel.AdGroup, "adgroup-1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = "zdarma",
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
            });

    private static AdExecutionResult Succeeded(string? before, string? after, string? resourceId = null) =>
        new(AdExecutionOutcome.Succeeded, before, after, resourceId, null, null);

    private static FakeAdActionExecutor NewFake() =>
        new FakeAdActionExecutor(AdPlatform.GoogleAds)
            .SeedAd(Account, "ad-1")
            .SeedNegativeKeywordTarget(Account, AdEntityLevel.AdGroup, "adgroup-1");

    private sealed class Suite(Func<IAdActionExecutor> create, AdAction? pauseSample = null, AdAction? negativeSample = null)
        : AdActionExecutorContractTests
    {
        protected override IAdActionExecutor CreateExecutor() => create();

        protected override AdAction SamplePauseAd() => pauseSample ?? Pause();

        protected override AdAction? SampleAddNegativeKeyword() => negativeSample ?? Negative();
    }

    /// <summary>Delegates to the fake except where a delegate overrides the call, to model one realistic adapter bug.</summary>
    private sealed class Mutant(
        IAdActionExecutor inner,
        Func<IAdActionExecutor, AdAction, CancellationToken, Task<AdTargetState>>? read = null,
        Func<IAdActionExecutor, AdAction, CancellationToken, Task<AdExecutionResult>>? execute = null,
        Func<IAdActionExecutor, AdAction, AdExecutionResult, CancellationToken, Task<AdExecutionResult>>? revert = null,
        IReadOnlySet<AdActionType>? supported = null) : IAdActionExecutor
    {
        public AdPlatform Platform => inner.Platform;
        public IReadOnlySet<AdActionType> SupportedActions => supported ?? inner.SupportedActions;

        public Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct) =>
            read is null ? inner.ReadCurrentAsync(action, ct) : read(inner, action, ct);

        public Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct) =>
            execute is null ? inner.ExecuteAsync(action, ct) : execute(inner, action, ct);

        public Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct) =>
            revert is null ? inner.RevertAsync(action, original, ct) : revert(inner, action, original, ct);
    }
}
