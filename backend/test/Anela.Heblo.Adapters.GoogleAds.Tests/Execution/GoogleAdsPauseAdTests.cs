using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsPauseAdTests
{
    [Fact]
    public async Task reads_the_current_status_of_the_ad()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task an_unknown_ad_does_not_exist()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("221~999"), CancellationToken.None);

        state.Should().Be(new AdTargetState(false, null, null));
    }

    [Fact]
    public async Task a_malformed_target_is_missing_without_asking_google()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("441 OR 1=1"), CancellationToken.None);

        state.Exists.Should().BeFalse();
        api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task pauses_the_ad_with_a_status_only_update()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Should().Be(new AdExecutionResult(
            AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused,
            "customers/1234567890/adGroupAds/221~441", result.PlatformResponseJson, null));
        var (service, request) = api.Mutations.Single();
        service.Should().Be("adGroupAds");
        request["validateOnly"]!.GetValue<bool>().Should().BeFalse();
        var operation = request["operations"]![0]!;
        operation["updateMask"]!.GetValue<string>().Should().Be("status");
        operation["update"]!["resourceName"]!.GetValue<string>().Should().Be("customers/1234567890/adGroupAds/221~441");
        operation["update"]!["status"]!.GetValue<string>().Should().Be("PAUSED");
        api.AdStatuses["221~441"].Should().Be("PAUSED");
    }

    [Fact]
    public async Task revert_enables_the_ad_again()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.PauseAd("221~441");
        var original = await executor.ExecuteAsync(action, CancellationToken.None);

        var reverted = await executor.RevertAsync(action, original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        (reverted.BeforeValue, reverted.AfterValue).Should().Be((AdActionValues.Paused, AdActionValues.Enabled));
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task a_rejection_is_a_failed_result_carrying_the_google_error()
    {
        var api = ExecutorHarness.SeededApi();
        api.RejectNextMutate("mutateError.RESOURCE_NOT_FOUND", "Resource was not found.");
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().StartWith("mutateError.RESOURCE_NOT_FOUND");
        api.AdStatuses["221~441"].Should().Be("ENABLED");
    }

    [Fact]
    public async Task refuses_an_action_for_another_account_without_calling_google()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441", account: "9999999999"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task revert_of_a_failed_pause_does_nothing()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var failed = new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, null, "x");

        var result = await executor.RevertAsync(ExecutorHarness.PauseAd("221~441"), failed, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }
}
