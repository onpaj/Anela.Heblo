using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppRepositoryUnknownContactFetchTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppConversation MakeConversation(string id, string contactId, DateTime updatedAt) =>
        new()
        {
            Id = id,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    private static SmartsuppContactData MakeContactData(string id, string? name = null, string? email = null) =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task UpsertConversationAsync_FetchesContactViaRest_WhenLocalContactMissing()
    {
        // Arrange
        await using var db = NewContext();
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeContactData("ct-unknown", name: "Michaela", email: "michaela@example.com"));

        var repo = new SmartsuppRepository(db, apiClient.Object, NullLogger<SmartsuppRepository>.Instance);
        var incoming = MakeConversation("conv-1", "ct-unknown", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — REST was called, the contact was staged, and the conversation kept its link
        apiClient.Verify(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()), Times.Once);
        var storedContact = await db.SmartsuppContacts.SingleAsync();
        storedContact.Name.Should().Be("Michaela");
        storedContact.Email.Should().Be("michaela@example.com");

        var storedConv = await db.SmartsuppConversations.SingleAsync();
        storedConv.ContactId.Should().Be("ct-unknown");
        storedConv.ContactName.Should().Be("Michaela");
        storedConv.ContactEmail.Should().Be("michaela@example.com");
    }

    [Fact]
    public async Task UpsertConversationAsync_WipesContactId_WhenRestReturnsNull()
    {
        // Arrange
        await using var db = NewContext();
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppContactData?)null);

        var repo = new SmartsuppRepository(db, apiClient.Object, NullLogger<SmartsuppRepository>.Instance);
        var incoming = MakeConversation("conv-1", "ct-gone", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — REST attempted, fell back to previous behavior (wipe FK)
        apiClient.Verify(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()), Times.Once);
        var storedConv = await db.SmartsuppConversations.SingleAsync();
        storedConv.ContactId.Should().BeNull();
        storedConv.ContactName.Should().BeNull();
        storedConv.ContactEmail.Should().BeNull();
    }

    [Fact]
    public async Task UpsertConversationAsync_WipesContactIdAndLogsWarning_WhenRestThrows()
    {
        // Arrange — REST blows up (e.g., 500). Webhook must still persist the conversation.
        await using var db = NewContext();
        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient
            .Setup(c => c.GetContactAsync("ct-broken", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp 500"));

        var repo = new SmartsuppRepository(db, apiClient.Object, NullLogger<SmartsuppRepository>.Instance);
        var incoming = MakeConversation("conv-1", "ct-broken", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — conversation saved without link; ready for the backfill job to retry later.
        var storedConv = await db.SmartsuppConversations.SingleAsync();
        storedConv.ContactId.Should().BeNull();
        storedConv.ContactName.Should().BeNull();
    }

    [Fact]
    public async Task UpsertConversationAsync_DoesNotCallRest_WhenContactAlreadyInDb()
    {
        // Arrange — happy path: contact already synced via contact.acquired earlier.
        await using var db = NewContext();
        db.SmartsuppContacts.Add(new SmartsuppContact
        {
            Id = "ct-known",
            Name = "Vendy",
            Email = "vendy@example.com",
            CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Unspecified),
        });
        await db.SaveChangesAsync();

        var apiClient = new Mock<ISmartsuppApiClient>(MockBehavior.Strict);
        var repo = new SmartsuppRepository(db, apiClient.Object, NullLogger<SmartsuppRepository>.Instance);
        var incoming = MakeConversation("conv-1", "ct-known", new DateTime(2026, 6, 8, 10, 0, 0));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — strict mock means any unexpected call fails the test; verifies REST was skipped.
        var storedConv = await db.SmartsuppConversations.SingleAsync();
        storedConv.ContactName.Should().Be("Vendy");
        storedConv.ContactEmail.Should().Be("vendy@example.com");
        storedConv.ContactId.Should().Be("ct-known");
    }

    [Fact]
    public async Task ListOrphanContactConversationIdsAsync_ReturnsOnlyConversationsWithNoNameOrEmail()
    {
        // Arrange
        await using var db = NewContext();
        var orphan = MakeConversation("c-orphan", "ct-x", new DateTime(2026, 6, 8, 10, 0, 0));
        orphan.ContactId = null;
        orphan.ContactName = null;
        orphan.ContactEmail = null;
        var named = MakeConversation("c-named", "ct-y", new DateTime(2026, 6, 8, 10, 0, 0));
        named.ContactName = "Jana";
        var emailed = MakeConversation("c-emailed", "ct-z", new DateTime(2026, 6, 8, 10, 0, 0));
        emailed.ContactEmail = "j@x.cz";
        db.SmartsuppConversations.AddRange(orphan, named, emailed);
        await db.SaveChangesAsync();

        var repo = new SmartsuppRepository(db, Mock.Of<ISmartsuppApiClient>(), NullLogger<SmartsuppRepository>.Instance);

        // Act
        var ids = await repo.ListOrphanContactConversationIdsAsync(CancellationToken.None);

        // Assert
        ids.Should().BeEquivalentTo(new[] { "c-orphan" });
    }
}
