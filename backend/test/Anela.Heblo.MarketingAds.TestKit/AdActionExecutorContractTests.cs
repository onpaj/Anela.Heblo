using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// Cross-platform semantics every IAdActionExecutor must satisfy (spec 4.3, 4.4, 12.2, 12.3). A
/// platform test project derives from this class with CreateExecutor() backed by a stateful fake
/// transport (a fresh one per call) that knows the sample targets and answers "not found" for
/// <see cref="MissingTargetExternalId"/>. Never override or skip a fact.
/// </summary>
public abstract class AdActionExecutorContractTests
{
    public const string MissingTargetExternalId = "heblo-contract-missing-target";

    protected abstract IAdActionExecutor CreateExecutor();
    protected abstract AdAction SamplePauseAd();
    protected abstract AdAction? SampleAddNegativeKeyword();

    [Fact]
    public void Platform_is_defined_and_SupportedActions_match_the_samples()
    {
        var executor = CreateExecutor();

        Enum.IsDefined(executor.Platform).Should().BeTrue();
        executor.SupportedActions.Should().Contain(AdActionType.PauseAd);
        executor.SupportedActions.Contains(AdActionType.AddNegativeKeyword)
            .Should().Be(SampleAddNegativeKeyword() is not null,
                "a platform supports AddNegativeKeyword exactly when it provides a sample for it");
    }

    [Fact]
    public void SamplePauseAd_follows_the_PauseAd_conventions()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();

        action.Type.Should().Be(AdActionType.PauseAd);
        action.Platform.Should().Be(executor.Platform);
        action.AccountExternalId.Should().NotBeNullOrWhiteSpace();
        action.TargetLevel.Should().Be(AdEntityLevel.Ad);
        action.TargetExternalId.Should().NotBeNullOrWhiteSpace();
        action.TargetExternalId.Should().NotBe(MissingTargetExternalId);
        action.OldValue.Should().Be(AdActionValues.Enabled);
        action.NewValue.Should().Be(AdActionValues.Paused);
        action.Payload.Count.Should().Be(0);
    }

    [Fact]
    public void SampleAddNegativeKeyword_follows_the_AddNegativeKeyword_conventions()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return; // the platform has no negative keywords (Meta)

        action.Type.Should().Be(AdActionType.AddNegativeKeyword);
        action.Platform.Should().Be(CreateExecutor().Platform);
        new[] { AdEntityLevel.Campaign, AdEntityLevel.AdGroup }.Should().Contain(action.TargetLevel);
        action.OldValue.Should().Be(AdActionValues.Absent);
        action.NewValue.Should().Be(AdActionValues.Present);
        action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var text).Should().BeTrue();
        text.Should().NotBeNullOrWhiteSpace();
        action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var matchType).Should().BeTrue();
        Enum.GetNames<KeywordMatchType>().Should().Contain(matchType);
    }

    [Fact]
    public async Task PauseAd_ReadCurrent_reports_an_existing_enabled_ad_before_Execute()
    {
        var state = await CreateExecutor().ReadCurrentAsync(SamplePauseAd(), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task PauseAd_Execute_pauses_the_ad_and_reports_the_before_and_after_values()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", result.Error);
        result.BeforeValue.Should().Be(AdActionValues.Enabled);
        result.AfterValue.Should().Be(AdActionValues.Paused);
        result.Error.Should().BeNull();
        after.CurrentValue.Should().Be(AdActionValues.Paused);
    }

    [Fact]
    public async Task PauseAd_Revert_restores_the_value_ReadCurrent_reported_before_Execute()
    {
        var executor = CreateExecutor();
        var action = SamplePauseAd();
        var before = await executor.ReadCurrentAsync(action, CancellationToken.None);

        var executed = await executor.ExecuteAsync(action, CancellationToken.None);
        var reverted = await executor.RevertAsync(action, executed, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", reverted.Error);
        after.CurrentValue.Should().Be(before.CurrentValue);
    }

    [Fact]
    public async Task AddNegativeKeyword_ReadCurrent_reports_Absent_before_Execute()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;

        var state = await CreateExecutor().ReadCurrentAsync(action, CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task AddNegativeKeyword_Execute_creates_the_criterion_and_returns_its_resource_id()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;
        var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(action, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", result.Error);
        result.BeforeValue.Should().Be(AdActionValues.Absent);
        result.AfterValue.Should().Be(AdActionValues.Present);
        result.PlatformResourceId.Should().NotBeNullOrWhiteSpace("revert removes the criterion by this id");
        after.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task AddNegativeKeyword_Revert_restores_the_value_ReadCurrent_reported_before_Execute()
    {
        var action = SampleAddNegativeKeyword();
        if (action is null)
            return;
        var executor = CreateExecutor();
        var before = await executor.ReadCurrentAsync(action, CancellationToken.None);

        var executed = await executor.ExecuteAsync(action, CancellationToken.None);
        var reverted = await executor.RevertAsync(action, executed, CancellationToken.None);
        var after = await executor.ReadCurrentAsync(action, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded, "the executor reported '{0}'", reverted.Error);
        after.CurrentValue.Should().Be(before.CurrentValue);
    }

    [Fact]
    public async Task Execute_does_not_compare_OldValue_itself_the_core_does_the_stale_check()
    {
        var action = SamplePauseAd() with { OldValue = AdActionValues.Paused };

        var result = await CreateExecutor().ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded,
            "executors do not compare OldValue (spec 12.2); the executor reported '{0}'", result.Error);
        result.BeforeValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task ReadCurrent_on_a_missing_target_reports_that_it_does_not_exist()
    {
        var action = SamplePauseAd() with { TargetExternalId = MissingTargetExternalId };

        var state = await CreateExecutor().ReadCurrentAsync(action, CancellationToken.None);

        state.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_on_a_missing_target_returns_Failed_instead_of_throwing()
    {
        var action = SamplePauseAd() with { TargetExternalId = MissingTargetExternalId };

        var result = await CreateExecutor().ExecuteAsync(action, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
