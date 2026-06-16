using System.Net;
using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.API.Webhooks.Smartsupp;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppWebhookControllerTests
{
    private const string Secret = "test-shared-secret";

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

    private async Task<List<SmartsuppWebhookAuditEntry>> ReadAuditEntriesAsync(SmartsuppWebhookFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await ctx.SmartsuppWebhookAuditEntries
            .OrderBy(e => e.ReceivedAt)
            .ToListAsync();
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureMissing()
    {
        using var factory = new SmartsuppWebhookFactory();
        var client = factory.CreateClient();
        var body = "{\"event\":\"conversation.opened\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.SignatureStatus.Should().Be(SmartsuppWebhookSignatureStatus.Missing);
        entries[0].ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.NotProcessed);
        entries[0].RawBody.Should().Be(body);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenSignatureWrong()
    {
        using var factory = new SmartsuppWebhookFactory();
        var client = factory.CreateClient();
        var body = "{\"event\":\"conversation.opened\",\"data\":{}}";

        var response = await client.SendAsync(BuildRequest(body, signature: "deadbeef"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.SignatureStatus.Should().Be(SmartsuppWebhookSignatureStatus.Mismatch);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenSignatureValidAndKnownEvent()
    {
        using var factory = new SmartsuppWebhookFactory();
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.opened",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {
                "conversation": {
                  "id": "c-int-1",
                  "status": "open",
                  "created_at": "2026-05-13T09:59:00Z",
                  "updated_at": "2026-05-13T10:00:00Z"
                }
              }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle();
        entries[0].SignatureStatus.Should().Be(SmartsuppWebhookSignatureStatus.Valid);
        entries[0].ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.Success);
        entries[0].EventName.Should().Be("conversation.opened");
        entries[0].RawBody.Should().Be(body);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenUnknownEvent_AndSignatureValid()
    {
        using var factory = new SmartsuppWebhookFactory();
        var client = factory.CreateClient();
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
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.Success);
    }

    [Fact]
    public async Task Receive_ReturnsOk_WhenJsonMalformed_AndSignatureValid()
    {
        using var factory = new SmartsuppWebhookFactory();
        var client = factory.CreateClient();
        var body = "not-json-at-all";

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.MalformedJson);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorized_WhenAppIdConfiguredAndMismatched()
    {
        using var factory = CreateFactoryWithAppId("expected-app");
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.opened",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "wrong-app",
              "data": { "id": "c1", "status": "open", "created_at": "2026-05-13T10:00:00Z", "updated_at": "2026-05-13T10:00:00Z" }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.SignatureStatus.Should().Be(SmartsuppWebhookSignatureStatus.AppIdMismatch);
    }

    [Fact]
    public async Task Receive_WritesAuditEntry_WhenHandlerThrows()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.ReplaceReactionsWithThrowing();
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.opened",
              "timestamp": "2026-05-13T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {
                "conversation": {
                  "id": "c-int-x",
                  "status": "open",
                  "created_at": "2026-05-13T10:00:00Z",
                  "updated_at": "2026-05-13T10:00:00Z"
                }
              }
            }
            """;
        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.HandlerException);
        entries[0].ProcessingError.Should().Contain("boom");
    }

    [Fact]
    public async Task Receive_ReturnsOkWithNoAudit_WhenEventIsInIgnoreList()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = "{\"event\":\"visitor.connected\"}";

        var response = await client.SendAsync(BuildRequest(body, signature: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_ProcessesNormally_WhenEventIsNotInIgnoreList()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = """
        {
          "event": "conversation.exploded",
          "timestamp": "2026-05-20T10:00:00Z",
          "account_id": "acc-1",
          "app_id": "app-1",
          "data": {}
        }
        """;

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle().Which.EventName.Should().Be("conversation.exploded");
    }

    [Fact]
    public async Task Receive_DoesNotFilter_WhenEventNameDiffersByCase()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = """
        {
          "event": "Visitor.Connected",
          "timestamp": "2026-05-20T10:00:00Z",
          "account_id": "acc-1",
          "app_id": "app-1",
          "data": {}
        }
        """;

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.SignatureStatus.Should().Be(SmartsuppWebhookSignatureStatus.Valid);
    }

    [Fact]
    public async Task Receive_AuditsMalformedJson_WhenIgnoreListConfigured()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = "not-json-at-all";

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.MalformedJson);
    }
}

public class SmartsuppWebhookFactory : HebloWebApplicationFactory
{
    private string? _webhookAppId;
    private bool _replaceReactionsWithThrowing;
    private List<string> _ignoredEventTypes = new();

    public SmartsuppWebhookFactory()
    {
        _webhookAppId = null;
    }

    public void SetWebhookAppId(string appId)
    {
        _webhookAppId = appId;
    }

    public void ReplaceReactionsWithThrowing() => _replaceReactionsWithThrowing = true;

    public void SetIgnoredEventTypes(IEnumerable<string> types) =>
        _ignoredEventTypes = types.ToList();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ISmartsuppWebhookMetrics, NoOpSmartsuppWebhookMetrics>();
        if (_replaceReactionsWithThrowing)
        {
            foreach (var d in services.Where(s => s.ServiceType ==
                typeof(Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions.ISmartsuppWebhookReaction))
                .ToList())
            {
                services.Remove(d);
            }
            services.AddScoped<Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions.ISmartsuppWebhookReaction,
                ThrowingReaction>();
        }
    }

    protected override void ConfigureTestWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["Smartsupp:WebhookSecret"] = "test-shared-secret",
                ["Smartsupp:WebhookAppId"] = _webhookAppId,
            };
            for (var i = 0; i < _ignoredEventTypes.Count; i++)
                dict[$"Smartsupp:IgnoredEventTypes:{i}"] = _ignoredEventTypes[i];
            config.AddInMemoryCollection(dict);
        });
    }
}

internal sealed class NoOpSmartsuppWebhookMetrics : ISmartsuppWebhookMetrics
{
    public void RecordReceived(string eventName, string outcome, double durationMs) { }
    public void RecordSignatureFailure(string reason) { }
    public void RecordPayloadBytes(int bytes) { }
    public void RecordTruncation(string field) { }
}

internal sealed class ThrowingReaction
    : Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions.ISmartsuppWebhookReaction
{
    public string EventName => "conversation.opened";
    public Task HandleAsync(
        Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions.WebhookEventContext ctx,
        CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
}
