using Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppSyncJobTests
{
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private SmartsuppSyncJob CreateJob() =>
        new(_apiClient.Object, _repo.Object, NullLogger<SmartsuppSyncJob>.Instance);

    private static SmartsuppConversationData MakeConversation(string id, string? contactId = null) =>
        new()
        {
            Id = id,
            Status = "open",
            Unread = false,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
            ContactId = contactId,
        };

    private static SmartsuppContactData MakeContact(string id, string? name = "Test User", string? email = "test@test.cz") =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
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
        _repo.Setup(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ExecuteAsync_UpsertsSinglePage_WhenAfterIsNull()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Petra");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.ContactName == "Petra"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PagesThrough_WhenAfterIsNotNull()
    {
        // Arrange
        _apiClient.SetupSequence(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = "cursor-page2",
                      Items = [MakeConversation("c1")]
                  });
        _apiClient.Setup(c => c.SearchConversationsAsync("cursor-page2", 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = null,
                      Items = [MakeConversation("c2")]
                  });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((SmartsuppContactData?)null);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c2"), It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.SearchConversationsAsync("cursor-page2", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FetchesContact_AndUpsertsIt()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Monča", "vexy@post.cz");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _apiClient.Verify(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertContactAsync(
            It.Is<SmartsuppContact>(c => c.Id == "ct1" && c.Name == "Monča" && c.Email == "vexy@post.cz"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CachesContact_AcrossConversationsInSameRun()
    {
        // Arrange — two conversations share the same contact_id
        var c1 = MakeConversation("conv1", contactId: "ct-shared");
        var c2 = MakeConversation("conv2", contactId: "ct-shared");
        var contact = MakeContact("ct-shared", "Shared User");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [c1, c2] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct-shared", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert — GetContactAsync called exactly once despite two conversations sharing the id
        _apiClient.Verify(c => c.GetContactAsync("ct-shared", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesContactName_ForConversationContactName()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", name: "Monča");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.ContactName == "Monča"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesContactNameNull_WhenContactFetchFails()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HttpRequestException("500"));
        SetupRepoDefaults();

        // Act — should not throw; warning is logged and processing continues
        await CreateJob().ExecuteAsync();

        // Assert — conversation still upserted, just without a name
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.ContactName == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MapsSubTypeBot_ToAuthorTypeBot()
    {
        // Arrange
        var conversation = MakeConversation("c1");
        var botMessage = new SmartsuppMessageData
        {
            Id = "m1",
            SubType = "bot",
            Content = "Vítejte!",
            TriggerName = "Uvítání",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync([botMessage]);
        _apiClient.Setup(c => c.GetContactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((SmartsuppContactData?)null);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertMessagesAsync("c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Any(m => m.Id == "m1"
                    && m.AuthorType == SmartsuppMessageAuthorType.Bot
                    && m.AuthorName == "Uvítání"
                    && m.SubType == "bot")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MapsSubTypeContact_ToAuthorTypeVisitor()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Jana");
        var visitorMessage = new SmartsuppMessageData
        {
            Id = "m2",
            SubType = "contact",
            Content = "Potřebuji pomoc.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync([visitorMessage]);
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertMessagesAsync("c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Any(m => m.Id == "m2"
                    && m.AuthorType == SmartsuppMessageAuthorType.Visitor
                    && m.AuthorName == "Jana")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAdvanceWatermark_WhenNoConversationsReturned()
    {
        // Arrange
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = new() });
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
