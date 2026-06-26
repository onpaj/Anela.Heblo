using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp.WebhookAudit;

public class ReplayWebhookEventHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit_{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Handle_DispatchesProcessWebhookEvent_AndIncrementsReplayCount()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        var body = """{"event":"conversation.opened","timestamp":"2026-05-13T10:00:00Z","account_id":"acc-1","app_id":"app-1","data":{"k":1}}""";
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = body,
            EventName = "conversation.opened",
            AccountId = "acc-1",
            AppId = "app-1",
            EventTimestamp = DateTime.Parse("2026-05-13T10:00:00Z").ToUniversalTime(),
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
        await ctx.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<ProcessWebhookEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessWebhookEventResponse { Handled = true });

        var handler = new ReplayWebhookEventHandler(ctx, mediator.Object);
        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = id, ReplayedBy = "ondra@anela.cz" }, default);

        response.Success.Should().BeTrue();
        response.ReplayCount.Should().Be(1);

        mediator.Verify(m => m.Send(It.Is<ProcessWebhookEventRequest>(r =>
            r.EventName == "conversation.opened" &&
            r.AccountId == "acc-1" &&
            r.AppId == "app-1" &&
            r.Data.GetProperty("k").GetInt32() == 1),
            It.IsAny<CancellationToken>()), Times.Once);

        var updated = await ctx.SmartsuppWebhookAuditEntries.SingleAsync();
        updated.ReplayCount.Should().Be(1);
        updated.LastReplayedAt.Should().NotBeNull();
        updated.LastReplayedBy.Should().Be("ondra@anela.cz");

        // Replay must not create a new audit row
        (await ctx.SmartsuppWebhookAuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenIdMissing()
    {
        using var ctx = CreateContext();
        var handler = new ReplayWebhookEventHandler(ctx, Mock.Of<IMediator>());

        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = Guid.NewGuid(), ReplayedBy = "x" }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidOperation_WhenRawBodyIsMalformedJson()
    {
        using var ctx = CreateContext();
        var id = Guid.NewGuid();
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = id,
            ReceivedAt = DateTime.UtcNow,
            RawBody = "not-json-at-all",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.MalformedJson,
        });
        await ctx.SaveChangesAsync();

        var handler = new ReplayWebhookEventHandler(ctx, Mock.Of<IMediator>());
        var response = await handler.Handle(
            new ReplayWebhookEventRequest { Id = id, ReplayedBy = "x" }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }
}
