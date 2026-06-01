using System.Net;
using Anela.Heblo.Tests.Common;
using FluentAssertions;

namespace Anela.Heblo.Tests;

/// <summary>
/// Integration tests for SPA fallback behaviour.
/// Verifies that non-GET/HEAD requests to SPA paths return 405 instead of 500
/// (issue #681: scanners/bots POSTing to /index.html caused unhandled 500 when
/// wwwroot/index.html is absent).
/// </summary>
[Collection("WebApp")]
public class SpaFallbackTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SpaFallbackTests(HebloWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task NonGetHead_Request_To_SpaPath_Returns_405(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/index.html");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            because: $"the SPA fallback must reject {method} requests before attempting to serve index.html");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task NonGetHead_Request_To_Any_SpaPath_Returns_405(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/some-spa-route");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            because: $"the SPA fallback must reject {method} requests to any unmatched path");
    }

    [Fact]
    public async Task Get_Request_To_SpaPath_Returns_503_When_IndexHtml_Missing()
    {
        // In the test environment, wwwroot/index.html does not exist (no frontend build).
        // The guard added in ConfigureSpaFallback (issue #667) must return 503 instead of
        // letting UseSpa throw InvalidOperationException (which would surface as a 500).
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            because: "when index.html is absent the SPA fallback must return 503, not 500");
    }

    [Fact]
    public async Task Get_Request_To_Nested_SpaRoute_Returns_503_When_IndexHtml_Missing()
    {
        var response = await _client.GetAsync("/some/nested/route");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            because: "all unmatched GET routes must return 503 when the frontend build is not deployed");
    }
}
