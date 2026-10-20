using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestKit;

public class FakeAdPlatformReadSourceTests
{
    private const string Account = "123";
    private static readonly DateOnly Date = new(2026, 10, 6);

    [Fact]
    public async Task An_unknown_account_yields_empty_lists()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        (await source.GetEntitiesAsync("other", CancellationToken.None)).Should().BeEmpty();
        (await source.GetDailyFactsAsync("other", Date, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Daily_facts_are_filtered_to_the_requested_date()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        (await source.GetDailyFactsAsync(Account, Date, CancellationToken.None)).Should().NotBeEmpty();
        (await source.GetDailyFactsAsync(Account, Date.AddDays(-1), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Change_events_older_than_since_are_filtered_out()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);
        var afterTheSampleEvent = new DateTimeOffset(Date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        (await source.GetChangeEventsAsync(Account, afterTheSampleEvent, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_capabilities_return_empty_lists_even_when_rows_were_added()
    {
        var source = new FakeAdPlatformReadSource(AdPlatform.MetaAds, new AdSourceCapabilities(false, false, null))
            .WithSearchTerms(Account, new AdSearchTermRow("ag", Date, "term", null, 1, 1, 1m, 0m, 0m, "CZK"))
            .WithChangeEvents(Account, new AdChangeEventRow("e1", DateTimeOffset.UnixEpoch.AddYears(56), null,
                AdChangeActorKind.Unknown, null, null, "X", null, null));

        (await source.GetSearchTermsAsync(Account, Date, CancellationToken.None)).Should().BeEmpty();
        (await source.GetChangeEventsAsync(Account, DateTimeOffset.UnixEpoch, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task FailWith_makes_every_call_throw_the_given_exception_after_recording_it()
    {
        var source = new FakeAdPlatformReadSource(AdPlatform.Sklik).FailWith(new HttpRequestException("401"));

        var act = () => source.GetAccountsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("401");
        source.Calls.Should().Equal("GetAccounts");
    }

    [Fact]
    public async Task Calls_record_each_request_in_order()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);

        await source.GetAccountsAsync(CancellationToken.None);
        await source.GetDailyFactsAsync(Account, Date, CancellationToken.None);

        source.Calls.Should().Equal("GetAccounts", "GetDailyFacts:123:2026-10-06");
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_returning_data()
    {
        var source = FakeAdPlatformReadSource.CreateSample(AdPlatform.GoogleAds, Account, Date);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => source.GetAccountsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
