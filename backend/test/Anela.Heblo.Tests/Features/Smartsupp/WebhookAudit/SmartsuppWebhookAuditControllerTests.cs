using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class SmartsuppWebhookAuditControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task List_ReturnsSeededEntries_AsSuperUser()
    {
        using var factory = new HebloWebApplicationFactory();
        var id = Guid.NewGuid();
        await factory.SeedDatabaseAsync(ctx =>
        {
            ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
            {
                Id = id,
                ReceivedAt = DateTime.UtcNow,
                EventName = "conversation.opened",
                RawBody = "{}",
                BodySizeBytes = 2,
                SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
                ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
            });
            return Task.CompletedTask;
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/admin/smartsupp/webhooks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListWebhookAuditResponse>(JsonOptions);
        body!.Items.Should().ContainSingle(e => e.Id == id);
    }
}
