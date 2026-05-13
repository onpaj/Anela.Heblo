using System.Net.Http;
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
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef>());
        _repo.Setup(r => r.MarkConversationResolvedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task Handle_ReconcilesLocallyOpenConversation_NotReturnedBySearch()
    {
        // Arrange
        var t0 = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified);
        var t1 = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Unspecified);

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [MakeConv("c-search", DateTime.UtcNow.AddMinutes(-5))] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetConversationAsync("c-stale", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData
            {
                Id = "c-stale",
                Status = "resolved",
                FinishedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = t0,
            });

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-search", t1), new("c-stale", t0) });

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c-stale" && c.Status == SmartsuppConversationStatus.Resolved && c.FinishedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
        response.ConversationsReconciled.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MarksLocallyOpenAsResolved_When404FromSmartsupp()
    {
        // Arrange
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
        _apiClient.Setup(c => c.GetConversationAsync("c-stale", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversationData?)null);

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-stale", null) });

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert
        _repo.Verify(r => r.MarkConversationResolvedAsync(
            "c-stale",
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        response.ConversationsClosedRemotely.Should().Be(1);
        response.ConversationsReconciled.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SkipsMessagesFetch_WhenStatusUnchangedAndLastMessageAtUnchanged()
    {
        // Arrange
        var lm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-10), DateTimeKind.Unspecified);

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
        _apiClient.Setup(c => c.GetConversationAsync("c-still-open", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData
            {
                Id = "c-still-open",
                Status = "open",
                LastMessageAt = lm,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = lm.AddHours(-1),
            });

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-still-open", lm) });

        // Act
        await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert: conversation row still upserted (IsServed etc. may change)
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c-still-open"),
            It.IsAny<CancellationToken>()), Times.Once);
        // Messages NOT re-fetched
        _apiClient.Verify(c => c.GetConversationMessagesAsync("c-still-open", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RefetchesMessages_WhenLastMessageAtAdvanced()
    {
        // Arrange
        var oldLm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Unspecified);
        var newLm = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
        _apiClient.Setup(c => c.GetConversationAsync("c-active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData
            {
                Id = "c-active",
                Status = "open",
                LastMessageAt = newLm,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = oldLm.AddHours(-1),
            });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c-active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>
            {
                new() { Id = "m-new", CreatedAt = newLm, UpdatedAt = newLm, SubType = "contact" }
            });

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-active", oldLm) });

        // Act
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert
        _apiClient.Verify(c => c.GetConversationMessagesAsync("c-active", It.IsAny<CancellationToken>()), Times.Once);
        response.MessagesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task Handle_DoesNotReFetchConversationsAlreadySeenInSearch()
    {
        // Arrange
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult
            {
                Total = 1, After = null,
                Items = [MakeConv("c-search", DateTime.UtcNow.AddMinutes(-5))]
            });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c-search", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppMessageData>());

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-search", DateTime.UtcNow.AddMinutes(-5)) });

        // Act
        await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert: GetConversationAsync never called because c-search was seen in search
        _apiClient.Verify(c => c.GetConversationAsync("c-search", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesReconciliation_WhenIndividualGetConversationFails()
    {
        // Arrange
        var t0 = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified);

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = [] });
        _apiClient.Setup(c => c.GetConversationAsync("c-fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network error"));
        _apiClient.Setup(c => c.GetConversationAsync("c-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData
            {
                Id = "c-ok",
                Status = "resolved",
                FinishedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = t0,
            });

        SetupRepoDefaults();
        _repo.Setup(r => r.ListOpenConversationRefsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OpenConversationRef> { new("c-fail", t0), new("c-ok", t0) });

        // Act — must not throw
        var response = await CreateHandler().Handle(new RunManualSyncRequest(), CancellationToken.None);

        // Assert: c-ok still processed despite c-fail blowing up
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c-ok" && c.Status == SmartsuppConversationStatus.Resolved),
            It.IsAny<CancellationToken>()), Times.Once);
        response.Success.Should().BeTrue();
    }
}
