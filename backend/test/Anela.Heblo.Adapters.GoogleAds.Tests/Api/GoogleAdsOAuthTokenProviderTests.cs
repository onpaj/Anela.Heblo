using System.Net;
using System.Web;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsOAuthTokenProviderTests
{
    private const string FirstToken = """{"access_token":"ya29.first","expires_in":3599,"token_type":"Bearer"}""";
    private const string SecondToken = """{"access_token":"ya29.second","expires_in":3599,"token_type":"Bearer"}""";

    [Fact]
    public async Task exchanges_the_refresh_token_once_and_reuses_the_access_token()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, FirstToken);
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("ya29.first");
        second.Should().Be("ya29.first");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be(new Uri("https://oauth2.googleapis.com/token"));
        var form = HttpUtility.ParseQueryString(handler.Requests[0].Body!);
        form["grant_type"].Should().Be("refresh_token");
        form["refresh_token"].Should().Be(TestSettings.RefreshToken);
        form["client_id"].Should().Be(TestSettings.OAuthClientId);
        form["client_secret"].Should().Be(TestSettings.OAuthClientSecret);
    }

    [Fact]
    public async Task refreshes_two_minutes_before_the_access_token_expires()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var time = new FakeTimeProvider(TestSettings.Now);
        var provider = Create(handler, time, TestSettings.Create());

        await provider.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(3599) - TimeSpan.FromMinutes(2));
        var refreshed = await provider.GetAccessTokenAsync(CancellationToken.None);

        refreshed.Should().Be("ya29.second");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task fetches_a_new_token_when_the_refresh_token_setting_changes()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var monitor = new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create());
        var provider = new GoogleAdsOAuthTokenProvider(
            new StubHttpClientFactory().With(GoogleAdsOAuthTokenProvider.HttpClientName, handler),
            monitor, new FakeTimeProvider(TestSettings.Now));

        await provider.GetAccessTokenAsync(CancellationToken.None);
        monitor.CurrentValue = TestSettings.Create(s => s.OAuth2RefreshToken = "rotated-refresh-token");
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        token.Should().Be("ya29.second");
        HttpUtility.ParseQueryString(handler.Requests[1].Body!)["refresh_token"].Should().Be("rotated-refresh-token");
    }

    [Fact]
    public async Task invalidating_the_cached_token_forces_a_new_exchange()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var revoked = await provider.GetAccessTokenAsync(CancellationToken.None);
        provider.Invalidate(revoked);
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        token.Should().Be("ya29.second");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task invalidating_a_stale_token_keeps_the_newer_cached_one()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var stale = await provider.GetAccessTokenAsync(CancellationToken.None);
        provider.Invalidate(stale);
        await provider.GetAccessTokenAsync(CancellationToken.None);
        provider.Invalidate(stale);
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        token.Should().Be("ya29.second");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task invalid_grant_throws_a_non_transient_error_that_does_not_leak_secrets()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        var error = (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which;
        error.ErrorCode.Should().Be("oauth.invalid_grant");
        error.IsTransient.Should().BeFalse();
        error.Message.Should().NotContain(TestSettings.RefreshToken).And.NotContain(TestSettings.OAuthClientSecret);
    }

    [Fact]
    public async Task a_5xx_from_the_token_endpoint_is_transient()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.IsTransient.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task a_timeout_or_rate_limit_from_the_token_endpoint_is_transient(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler().Enqueue(status, """{"error":"rate_limit_exceeded"}""");
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.IsTransient.Should().BeTrue();
    }

    private static GoogleAdsOAuthTokenProvider Create(
        StubHttpMessageHandler handler, TimeProvider time, GoogleAdsSettings settings) =>
        new(new StubHttpClientFactory().With(GoogleAdsOAuthTokenProvider.HttpClientName, handler),
            new TestOptionsMonitor<GoogleAdsSettings>(settings), time);
}
