using System.Net;
using System.Text;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudTokenRefreshClientTests
{
    private const string RefreshUrl =
        "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh";

    private const string ValidResponseJson = """
        {
          "access_token": "new-access",
          "refresh_token": "new-refresh",
          "expires_at": 9999999999
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
        var sut = CreateClient(HttpStatusCode.OK, ValidResponseJson);

        var result = await sut.RefreshAsync("old-refresh");

        result.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.ExpiresAt.Should().Be(9999999999L);
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenResponseIsNonSuccess()
    {
        var sut = CreateClient(HttpStatusCode.Unauthorized, "Unauthorized");

        var act = () => sut.RefreshAsync("old-refresh");

        await act.Should().ThrowAsync<HttpRequestException>();
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
    public async Task RefreshAsync_SendsRefreshTokenInBody()
    {
        var capturer = new CapturingHandler(ValidResponseJson);
        var sut = new PlaudTokenRefreshClient(new HttpClient(capturer));

        await sut.RefreshAsync("my-refresh-token");

        capturer.CapturedRequestBody.Should().Contain("my-refresh-token");
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestUri = request.RequestUri;
            if (request.Content != null)
            {
                await request.Content.LoadIntoBufferAsync();
                CapturedRequestBody = await request.Content.ReadAsStringAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
