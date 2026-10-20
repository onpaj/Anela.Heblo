using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsValidateOnlyTests
{
    [Fact]
    public async Task a_validated_pause_reports_failed_with_the_marker_and_changes_nothing()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: true);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        result.PlatformResourceId.Should().BeNull();
        api.Mutations.Single().Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.AdStatuses["221~441"].Should().Be("ENABLED");
    }

    [Fact]
    public async Task a_validated_negative_keyword_is_not_created()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: true);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", "zdarma", "Exact");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        api.Mutations.Single().Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.Negatives.Should().BeEmpty();
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task a_rejection_in_validate_only_mode_reports_the_google_error_not_the_marker()
    {
        var api = ExecutorHarness.SeededApi();
        api.RejectNextMutate("policyViolationError.POLICY_ERROR", "Policy violation.");
        var executor = ExecutorHarness.Create(api, validateOnly: true);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Error.Should().StartWith("policyViolationError.POLICY_ERROR");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Development")]
    public async Task outside_production_a_validate_only_override_to_false_is_ignored(string environment)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: false, environment: environment);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        api.Mutations.Single().Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.AdStatuses["221~441"].Should().Be("ENABLED");
    }

    [Fact]
    public async Task outside_production_a_revert_is_validate_only_too()
    {
        var api = ExecutorHarness.SeededApi();
        var original = await ExecutorHarness.Create(api).ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);
        var staging = ExecutorHarness.Create(api, validateOnly: false, environment: "Staging");

        var reverted = await staging.RevertAsync(ExecutorHarness.PauseAd("221~441"), original, CancellationToken.None);

        reverted.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        api.AdStatuses["221~441"].Should().Be("PAUSED");
    }
}
