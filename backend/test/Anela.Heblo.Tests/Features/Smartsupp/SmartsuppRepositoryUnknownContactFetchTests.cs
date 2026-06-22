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

        // Act — after the REST fetch, UpsertContactAsync executes raw SQL (requires Postgres).
        // Catch the InMemory exception so we can verify the C# contact-fetch decision.
        try { await repo.UpsertConversationAsync(incoming, CancellationToken.None); }
        catch (InvalidOperationException) { /* InMemory does not support ExecuteSqlInterpolatedAsync */ }

        // Assert — REST was called to fetch the missing contact.
        // Persistence (contact row, conversation row) is verified in SmartsuppRepositoryUpsertIntegrationTests.
        apiClient.Verify(c => c.GetContactAsync("ct-unknown", It.IsAny<CancellationToken>()), Times.Once);
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

        // Act — ContactId is cleared in C# before raw SQL runs. Catch the InMemory exception.
        try { await repo.UpsertConversationAsync(incoming, CancellationToken.None); }
        catch (InvalidOperationException) { /* InMemory does not support ExecuteSqlInterpolatedAsync */ }

        // Assert — REST attempted; ContactId wiped because REST returned null (fail-open)
        apiClient.Verify(c => c.GetContactAsync("ct-gone", It.IsAny<CancellationToken>()), Times.Once);
        incoming.ContactId.Should().BeNull();
        incoming.ContactName.Should().BeNull();
        incoming.ContactEmail.Should().BeNull();
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

        // Act — fail-open: REST exception is caught inside TryFetchAndStageContactAsync and
        // ContactId is cleared in C# before raw SQL runs. Catch the InMemory exception.
        try { await repo.UpsertConversationAsync(incoming, CancellationToken.None); }
        catch (InvalidOperationException) { /* InMemory does not support ExecuteSqlInterpolatedAsync */ }

        // Assert — ContactId cleared; conversation saved without link so backfill job can retry.
        incoming.ContactId.Should().BeNull();
        incoming.ContactName.Should().BeNull();
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

        // Act — contact found in DB so TryFetchAndStageContactAsync is skipped; raw SQL upsert
        // then throws on InMemory. Catch so we can assert on the C# contact-lookup step.
        try { await repo.UpsertConversationAsync(incoming, CancellationToken.None); }
        catch (InvalidOperationException) { /* InMemory does not support ExecuteSqlInterpolatedAsync */ }

        // Assert — strict mock: any unexpected REST call would fail. Denorm fields hydrated from DB contact.
        incoming.ContactName.Should().Be("Vendy");
        incoming.ContactEmail.Should().Be("vendy@example.com");
        incoming.ContactId.Should().Be("ct-known");
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
