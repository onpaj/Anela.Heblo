using System.Net;
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsRestClientTests
{
    private static readonly GoogleAdsQuery Query = new("campaigns", "SELECT campaign.id FROM campaign");
    private const string OneRow = """{"results":[{"campaign":{"resourceName":"customers/1234567890/campaigns/111","id":"111"}}]}""";
    private const string Forbidden = """
        {"error":{"code":403,"message":"denied","status":"PERMISSION_DENIED","details":[{
          "@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
          "errors":[{"errorCode":{"authorizationError":"USER_PERMISSION_DENIED"},"message":"User doesn't have permission."}],
          "requestId":"r-1"}]}}
        """;
    private const string Unauthenticated =
        """{"error":{"code":401,"message":"Request had invalid authentication credentials.","status":"UNAUTHENTICATED"}}""";

    [Fact]
    public async Task posts_the_query_to_the_versioned_search_endpoint_with_bearer_and_login_header()
    {
        var (client, handler) = Create(s => s.LoginCustomerId = "111-222-3333");
        handler.Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].GetProperty("campaign").GetProperty("id").GetString().Should().Be("111");
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://googleads.googleapis.com/v25/customers/1234567890/googleAds:search"));
        request.Headers["Authorization"].Should().Be($"Bearer {StaticAccessTokenProvider.Token}");
        request.Headers["login-customer-id"].Should().Be("1112223333");
        request.Headers.Should().NotContainKey("developer-token");
        JsonNode.Parse(request.Body!)!["query"]!.GetValue<string>().Should().Be(Query.Gaql);
    }

    [Fact]
    public async Task omits_the_login_customer_id_header_for_direct_access()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OneRow);

        await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        handler.Requests.Single().Headers.Should().NotContainKey("login-customer-id");
    }

    [Fact]
    public async Task follows_next_page_tokens_and_concatenates_the_pages()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"campaign":{"id":"1"}}],"nextPageToken":"p2"}""")
               .Enqueue(HttpStatusCode.OK, """{"results":[{"campaign":{"id":"2"}}]}""");

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Select(r => r.GetProperty("campaign").GetProperty("id").GetString()).Should().Equal("1", "2");
        JsonNode.Parse(handler.Requests[0].Body!)!["pageToken"].Should().BeNull();
        JsonNode.Parse(handler.Requests[1].Body!)!["pageToken"]!.GetValue<string>().Should().Be("p2");
    }

    [Fact]
    public async Task an_empty_result_has_no_results_key_and_yields_no_rows()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"fieldMask":"campaign.id"}""");

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task retries_a_503_and_then_succeeds()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE","message":"busy"}}""")
               .Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task does_not_retry_a_403_and_surfaces_the_google_error_code()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Forbidden, Forbidden);

        var act = () => client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        var error = (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which;
        error.ErrorCode.Should().Be("authorizationError.USER_PERMISSION_DENIED");
        error.RequestId.Should().Be("r-1");
        error.IsTransient.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task a_401_invalidates_the_access_token_and_resends_once()
    {
        var tokens = new StaticAccessTokenProvider();
        var (client, handler) = Create(tokens: tokens);
        handler.Enqueue(HttpStatusCode.Unauthorized, Unauthenticated)
               .Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
        tokens.Invalidated.Should().Equal(StaticAccessTokenProvider.Token);
    }

    [Fact]
    public async Task a_second_401_is_surfaced_as_a_permanent_error()
    {
        var tokens = new StaticAccessTokenProvider();
        var (client, handler) = Create(tokens: tokens);
        handler.Enqueue(HttpStatusCode.Unauthorized, Unauthenticated)
               .Enqueue(HttpStatusCode.Unauthorized, Unauthenticated);

        var act = () => client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.IsTransient.Should().BeFalse();
        handler.Requests.Should().HaveCount(2);
        tokens.Invalidated.Should().ContainSingle();
    }

    [Fact]
    public async Task retries_an_http_client_timeout()
    {
        var (client, handler) = Create();
        handler.Enqueue(_ => throw new TaskCanceledException("The request was canceled due to HttpClient.Timeout"))
               .Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task does_not_retry_when_the_caller_cancels()
    {
        var (client, handler) = Create();
        using var cts = new CancellationTokenSource();
        handler.Enqueue(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var act = () => client.SearchAsync(TestSettings.CustomerId, Query, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task rejects_a_non_numeric_customer_id_without_calling_google()
    {
        var (client, handler) = Create();

        var act = () => client.SearchAsync("123/../../evil", Query, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    internal static (GoogleAdsRestClient Client, StubHttpMessageHandler Handler) Create(
        Action<GoogleAdsSettings>? configure = null, StaticAccessTokenProvider? tokens = null)
    {
        var handler = new StubHttpMessageHandler();
        var client = new GoogleAdsRestClient(
            new StubHttpClientFactory().With(GoogleAdsRestClient.HttpClientName, handler),
            tokens ?? new StaticAccessTokenProvider(),
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(configure)),
            NullLogger<GoogleAdsRestClient>.Instance,
            retryDelay: TimeSpan.Zero);
        return (client, handler);
    }
}
