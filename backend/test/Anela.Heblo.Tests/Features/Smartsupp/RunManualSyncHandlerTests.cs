using Anela.Heblo.Application.Features.Smartsupp.UseCases.RunManualSync;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RunManualSyncHandlerTests
{
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private RunManualSyncHandler CreateHandler() =>
        new(_apiClient.Object, _repo.Object, NullLogger<RunManualSyncHandler>.Instance);

    private static SmartsuppConversationData MakeConv(string id, DateTime updatedAt, string? contactId = null) =>
        new()
        {
            Id = id,
            Status = "open",
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            ContactId = contactId,
        };

    private void SetupRepoDefaults()
    {
        _repo.Setup(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_DefaultsSinceToSevenDaysAgo_WhenNotProvided()
    {
        var recent = MakeConv("c-recent", DateTime.UtcNow.AddDays(-2));
        var stale = MakeConv("c-stale", DateTime.UtcNow.AddDays(-10));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [recent, stale] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-recent"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-stale"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HonoursSince_WhenProvided()
    {
        var since = DateTime.UtcNow.AddHours(-1);
        var inRange = MakeConv("c1", DateTime.UtcNow.AddMinutes(-30));
        var outOfRange = MakeConv("c2", DateTime.UtcNow.AddHours(-2));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [inRange, outOfRange] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        var response = await CreateHandler().Handle(new RunManualSyncRequest { Since = since }, CancellationToken.None);

        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CountsMessagesAcrossConversations()
    {
        var c1 = MakeConv("c1", DateTime.UtcNow.AddMinutes(-10));
        var c2 = MakeConv("c2", DateTime.UtcNow.AddMinutes(-5));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [c1, c2] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>
            {
                new() { Id = "m1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "contact" },
                new() { Id = "m2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "agent" },
            });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>
            {
                new() { Id = "m3", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubType = "contact" },
            });
        SetupRepoDefaults();

        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        response.ConversationsProcessed.Should().Be(2);
        response.MessagesProcessed.Should().Be(3);
    }

    [Fact]
    public async Task Handle_PagesThroughAllResults()
    {
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = "cursor", Items = [MakeConv("c1", DateTime.UtcNow.AddMinutes(-1))] });
        _apiClient.Setup(c => c.SearchConversationsAsync("cursor", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [MakeConv("c2", DateTime.UtcNow.AddMinutes(-2))] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        response.ConversationsProcessed.Should().Be(2);
        _apiClient.Verify(c => c.SearchConversationsAsync("cursor", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClampsSinceToThirtyDaysAgo_WhenTooDeep()
    {
        var deepSince = DateTime.UtcNow.AddDays(-90);
        var withinThirty = MakeConv("c-within", DateTime.UtcNow.AddDays(-20));
        var beyondThirty = MakeConv("c-beyond", DateTime.UtcNow.AddDays(-40));

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [withinThirty, beyondThirty] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());
        SetupRepoDefaults();

        var response = await CreateHandler().Handle(new RunManualSyncRequest { Since = deepSince }, CancellationToken.None);

        response.ConversationsProcessed.Should().Be(1);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-within"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c-beyond"), It.IsAny<CancellationToken>()), Times.Never);
    }
}
