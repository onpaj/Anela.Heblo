using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class GetWebhookAuditEntryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Handle_ReturnsEntry_WhenIdExists()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = "{\"k\":1}",
            HeadersJson = "{\"x-foo\":\"bar\"}",
            BodySizeBytes = 7,
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
        await ctx.SaveChangesAsync();

        var response = await new GetWebhookAuditEntryHandler(ctx)
            .Handle(new GetWebhookAuditEntryRequest { Id = id }, default);

        response.Success.Should().BeTrue();
        response.Entry!.RawBody.Should().Be("{\"k\":1}");
        response.Entry.HeadersJson.Should().Be("{\"x-foo\":\"bar\"}");
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenIdMissing()
    {
        using var ctx = CreateContext();
        var response = await new GetWebhookAuditEntryHandler(ctx)
            .Handle(new GetWebhookAuditEntryRequest { Id = Guid.NewGuid() }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        response.Entry.Should().BeNull();
    }
}
