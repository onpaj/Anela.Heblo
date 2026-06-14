using System.Net;
using System.Text;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenRefreshClientTests
{
    private const string RefreshUrl =
        "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh";

    private const long ExpiresInSeconds = 3600L;

    private const string ValidResponseJson = """
        {
          "access_token": "new-access",
          "refresh_token": "new-refresh",
          "token_type": "bearer",
          "expires_in": 3600
        }
        """;

    private static PlaudTokenRefreshClient CreateClient(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return new PlaudTokenRefreshClient(new HttpClient(handler));
    }

    [Fact]
    public async Task RefreshAsync_ReturnsTokens_WhenResponseIsValid()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sut = CreateClient(HttpStatusCode.OK, ValidResponseJson);

        var result = await sut.RefreshAsync("old-refresh");

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        result.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.TokenType.Should().Be("bearer");
        // expires_in (seconds) is converted to an absolute Unix millisecond timestamp.
        result.ExpiresAt.Should().BeInRange(
            before + ExpiresInSeconds * 1000L,
            after + ExpiresInSeconds * 1000L);
    }

    [Fact]
    public async Task RefreshAsync_FallsBackToInputRefreshToken_WhenResponseOmitsIt()
    {
        const string responseWithoutRefresh = """
            { "access_token": "new-access", "token_type": "bearer", "expires_in": 3600 }
            """;
        var sut = CreateClient(HttpStatusCode.OK, responseWithoutRefresh);

        var result = await sut.RefreshAsync("old-refresh");

        result.RefreshToken.Should().Be("old-refresh");
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenResponseIsNonSuccess()
    {
        var sut = CreateClient(HttpStatusCode.Unauthorized, "Unauthorized");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RefreshAsync_IncludesStatusAndBody_WhenUnprocessableEntity()
    {
        var sut = CreateClient((HttpStatusCode)422, "field required");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*422*field required*");
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenResponseBodyIsEmpty()
    {
        var sut = CreateClient(HttpStatusCode.OK, "null");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Empty refresh response*");
    }

    [Fact]
    public async Task RefreshAsync_SendsFormUrlEncodedRefreshToken()
    {
        var capturer = new CapturingHandler(ValidResponseJson);
        var sut = new PlaudTokenRefreshClient(new HttpClient(capturer));

        await sut.RefreshAsync("my-refresh-token");

        capturer.CapturedContentType.Should().Be("application/x-www-form-urlencoded");
        capturer.CapturedRequestBody.Should().Be("refresh_token=my-refresh-token");
        capturer.CapturedRequestUri!.ToString().Should().Be(RefreshUrl);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }
        public Uri? CapturedRequestUri { get; private set; }
        public string? CapturedContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestUri = request.RequestUri;
            if (request.Content != null)
            {
                await request.Content.LoadIntoBufferAsync();
                CapturedRequestBody = await request.Content.ReadAsStringAsync(ct);
                CapturedContentType = request.Content.Headers.ContentType?.MediaType;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
