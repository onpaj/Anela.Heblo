using System.Net;
using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppWebhookControllerTests : IClassFixture<SmartsuppWebhookFactory>
{
    private const string Secret = "test-shared-secret";

    private readonly SmartsuppWebhookFactory _factory;

    public SmartsuppWebhookControllerTests(SmartsuppWebhookFactory factory)
    {
        _factory = factory;
    }

    private SmartsuppWebhookFactory CreateFactoryWithAppId(string appId)
    {
        var factory = new SmartsuppWebhookFactory();
        factory.SetWebhookAppId(appId);
        return factory;
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpRequestMessage BuildRequest(string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/smartsupp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature != null)
            request.Headers.Add("X-Smartsupp-Hmac", signature);
        return request;
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureMissing()
    {
        var client = _factory.CreateClient();
        var body = "{\"event\":\"conversation.created\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureWrong()
    {
        var client = _factory.CreateClient();
        var body = "{\"event\":\"conversation.created\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: "deadbeef"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenSignatureValidAndKnownEvent()
    {
        var client = _factory.CreateClient();
        var body = """
            {
              "event": "conversation.created",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {
                "id": "c-int-1",
                "status": "open",
                "created_at": "2026-05-13T09:59:00Z",
                "updated_at": "2026-05-13T10:00:00Z"
              }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenUnknownEvent_AndSignatureValid()
    {
        var client = _factory.CreateClient();
        var body = """
            {
              "event": "conversation.exploded",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {}
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenJsonMalformed_AndSignatureValid()
    {
        var client = _factory.CreateClient();
        var body = "not-json-at-all";

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenAppIdConfiguredAndMismatched()
    {
        var factory = CreateFactoryWithAppId("expected-app");
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.created",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "wrong-app",
              "data": { "id": "c1", "status": "open", "created_at": "2026-05-13T10:00:00Z", "updated_at": "2026-05-13T10:00:00Z" }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class SmartsuppWebhookFactory : HebloWebApplicationFactory
{
    private string? _webhookAppId;

    public SmartsuppWebhookFactory()
    {
        _webhookAppId = null;
    }

    public void SetWebhookAppId(string appId)
    {
        _webhookAppId = appId;
    }

    protected override void ConfigureTestWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smartsupp:WebhookSecret"] = "test-shared-secret",
                ["Smartsupp:WebhookAppId"] = _webhookAppId,
            });
        });
    }
}
