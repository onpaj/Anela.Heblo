using System.Net;
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsRestClientMutateTests
{
    private const string Created = """{"results":[{"resourceName":"customers/1234567890/campaignCriteria/111~9001"}]}""";
    private const string Duplicate = """
        {"error":{"code":400,"message":"Request contains an invalid argument.","status":"INVALID_ARGUMENT","details":[{
          "@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
          "errors":[{"errorCode":{"criterionError":"KEYWORD_HAS_INVALID_CHARS"},"message":"Keyword has invalid characters."}],
          "requestId":"m-1"}]}}
        """;

    private static JsonObject Request() => new()
    {
        ["operations"] = new JsonArray(new JsonObject { ["remove"] = "customers/1234567890/campaignCriteria/111~9001" }),
        ["partialFailure"] = false,
        ["validateOnly"] = false,
    };

    [Fact]
    public async Task posts_to_the_service_mutate_endpoint_and_returns_resource_names()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.OK, Created);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ResourceNames.Should().Equal("customers/1234567890/campaignCriteria/111~9001");
        var request = handler.Requests.Single();
        request.Uri.Should().Be(new Uri("https://googleads.googleapis.com/v25/customers/1234567890/campaignCriteria:mutate"));
        JsonNode.Parse(request.Body!)!["operations"]![0]!["remove"]!.GetValue<string>()
            .Should().Be("customers/1234567890/campaignCriteria/111~9001");
    }

    [Fact]
    public async Task a_validate_only_response_has_no_resource_names()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.AdGroupAds, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ResourceNames.Should().BeEmpty();
    }

    [Fact]
    public async Task returns_a_rejection_instead_of_throwing_for_a_400()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.BadRequest, Duplicate);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("criterionError.KEYWORD_HAS_INVALID_CHARS");
        result.ErrorMessage.Should().Be("Keyword has invalid characters.");
        JsonNode.Parse(result.ResponseJson)!["error"]!["code"]!.GetValue<int>().Should().Be(400);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task does_not_retry_a_mutate_that_failed_with_503()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE","message":"busy"}}""")
               .Enqueue(HttpStatusCode.OK, Created);

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Requests.Should().ContainSingle("a 5xx mutate may already have been applied");
    }

    [Fact]
    public async Task does_not_retry_a_mutate_that_timed_out()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(_ => throw new TaskCanceledException("HttpClient.Timeout"))
               .Enqueue(HttpStatusCode.OK, Created);

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task retries_a_mutate_rejected_with_429()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"slow down"}}""")
               .Enqueue(HttpStatusCode.OK, Created);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task throws_for_auth_failures()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED","message":"no"}}""");

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.AdGroupAds, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<GoogleAdsApiException>();
    }

    [Fact]
    public async Task refuses_an_unknown_service_without_calling_google()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();

        var act = () => client.MutateAsync(TestSettings.CustomerId, "campaignBudgets", Request(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task retries_a_mutate_once_after_a_401_with_a_fresh_token()
    {
        var tokens = new StaticAccessTokenProvider();
        var (client, handler) = GoogleAdsRestClientTests.Create(tokens: tokens);
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"error":{"code":401,"status":"UNAUTHENTICATED","message":"expired"}}""")
               .Enqueue(HttpStatusCode.OK, Created);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        tokens.Invalidated.Should().Equal(StaticAccessTokenProvider.Token);
    }
}
