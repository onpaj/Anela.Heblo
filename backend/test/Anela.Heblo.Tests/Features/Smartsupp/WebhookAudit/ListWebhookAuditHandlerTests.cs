using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class ListWebhookAuditHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    private static SmartsuppWebhookAuditEntry MakeEntry(
        DateTime receivedAt,
        string eventName,
        SmartsuppWebhookProcessingStatus status) => new()
        {
            Id = Guid.NewGuid(),
            ReceivedAt = receivedAt,
            EventName = eventName,
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = status,
            RawBody = "{}",
            BodySizeBytes = 2,
        };

    [Fact]
    public async Task Handle_ReturnsRowsOrderedByReceivedAtDescending()
    {
        using var ctx = CreateContext();
        ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddMinutes(-2), "a", SmartsuppWebhookProcessingStatus.Success));
        ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddMinutes(-1), "b", SmartsuppWebhookProcessingStatus.Success));
        await ctx.SaveChangesAsync();

        var handler = new ListWebhookAuditHandler(ctx);
        var response = await handler.Handle(new ListWebhookAuditRequest(), default);

        response.Items.Should().HaveCount(2);
        response.Items[0].EventName.Should().Be("b");
        response.Items[1].EventName.Should().Be("a");
        response.Total.Should().Be(2);
    }

    [Fact]
    public async Task Handle_FiltersByEventNameAndStatus()
    {
        using var ctx = CreateContext();
        ctx.SmartsuppWebhookAuditEntries.AddRange(
            MakeEntry(DateTime.UtcNow, "conv.opened", SmartsuppWebhookProcessingStatus.Success),
            MakeEntry(DateTime.UtcNow, "conv.opened", SmartsuppWebhookProcessingStatus.HandlerException),
            MakeEntry(DateTime.UtcNow, "conv.closed", SmartsuppWebhookProcessingStatus.Success));
        await ctx.SaveChangesAsync();

        var handler = new ListWebhookAuditHandler(ctx);
        var response = await handler.Handle(new ListWebhookAuditRequest
        {
            EventName = "conv.opened",
            ProcessingStatus = SmartsuppWebhookProcessingStatus.HandlerException,
        }, default);

        response.Items.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.HandlerException);
    }

    [Fact]
    public async Task Handle_CapsTakeAt200()
    {
        using var ctx = CreateContext();
        for (var i = 0; i < 5; i++)
            ctx.SmartsuppWebhookAuditEntries.Add(MakeEntry(DateTime.UtcNow.AddSeconds(-i), $"e{i}", SmartsuppWebhookProcessingStatus.Success));
        await ctx.SaveChangesAsync();

        var handler = new ListWebhookAuditHandler(ctx);
        var response = await handler.Handle(new ListWebhookAuditRequest { Take = 9999 }, default);

        response.PageSize.Should().Be(200);
    }
}
