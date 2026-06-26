using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

internal static class SmartsuppRepositoryTestFactory
{
    public static SmartsuppRepository New(ApplicationDbContext db, ISmartsuppApiClient? apiClient = null) =>
        new(db, apiClient ?? Mock.Of<ISmartsuppApiClient>(), NullLogger<SmartsuppRepository>.Instance);
}

public class SmartsuppRepositoryMessageDeliveryTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task UpdateMessageDeliveryStatusAsync_UpdatesDeliveryFields_WhenMessageExists()
    {
        await using var db = NewContext();
        var msg = new SmartsuppMessage
        {
            Id = "m1",
            ConversationId = "c1",
            AuthorType = SmartsuppMessageAuthorType.Agent,
            DeliveryStatus = "pending",
            CreatedAt = new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified),
        };
        db.SmartsuppMessages.Add(msg);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var deliveredAt = new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified);
        await repo.UpdateMessageDeliveryStatusAsync("m1", "delivered", deliveredAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.SmartsuppMessages.SingleAsync();
        stored.DeliveryStatus.Should().Be("delivered");
        stored.DeliveredAt.Should().Be(deliveredAt);
    }

    [Fact]
    public async Task UpdateMessageDeliveryStatusAsync_DoesNotThrow_WhenMessageNotFound()
    {
        await using var db = NewContext();
        var repo = SmartsuppRepositoryTestFactory.New(db);

        var act = async () => await repo.UpdateMessageDeliveryStatusAsync("nonexistent", "delivered", null, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

public class SmartsuppRepositoryDenormFieldTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppConversation MakeConversation(
        string id,
        string? contactName = null,
        string? contactEmail = null,
        string? contactId = null) =>
        new()
        {
            Id = id,
            Status = SmartsuppConversationStatus.Open,
            ContactName = contactName,
            ContactEmail = contactEmail,
            ContactId = contactId,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 20, 11, 0, 0, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Unspecified),
        };

    private static SmartsuppContact MakeContact(string id, string? name = null, string? email = null) =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            UpdatedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
            SyncedAt = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Unspecified),
        };

    // UpsertConversationAsync_PreservesDenormFields_WhenNewValuesAreNull was removed:
    // the SQL COALESCE null-preservation is now tested in
    // SmartsuppRepositoryUpsertIntegrationTests.UpsertConversationAsync_NullContactName_DoesNotOverwriteStoredNonNullValue.

    [Fact]
    public async Task UpsertConversationAsync_HydratesDenormFields_FromExistingContact()
    {
        // Arrange
        await using var db = NewContext();
        var contact = MakeContact("c-contact-1", name: "Petr Novák", email: "petr@x.cz");
        db.SmartsuppContacts.Add(contact);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeConversation("conv-1", contactName: null, contactEmail: null, contactId: "c-contact-1");

        // Act — UpsertConversationAsync reads the linked contact via EF (InMemory) then executes
        // raw SQL (requires Postgres). Catch the InMemory exception so we can assert on the
        // C# hydration step that runs before the SQL call.
        try { await repo.UpsertConversationAsync(incoming, CancellationToken.None); }
        catch (InvalidOperationException) { /* InMemory does not support ExecuteSqlInterpolatedAsync */ }

        // Assert — contact name/email were hydrated from the linked contact in the C# layer
        incoming.ContactName.Should().Be("Petr Novák");
        incoming.ContactEmail.Should().Be("petr@x.cz");
        incoming.ContactId.Should().Be("c-contact-1");
    }

    [Fact]
    public async Task BackfillConversationDenormFieldsAsync_UpdatesConversations_WithContactNameAndEmail()
    {
        // Arrange
        await using var db = NewContext();
        var conv = MakeConversation("conv-backfill", contactName: null, contactEmail: null, contactId: "c-backfill");
        db.SmartsuppConversations.Add(conv);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var contact = MakeContact("c-backfill", name: "Marie Svobodová", email: "marie@x.cz");

        // Act
        await repo.BackfillConversationDenormFieldsAsync(contact, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Marie Svobodová");
        stored.ContactEmail.Should().Be("marie@x.cz");
    }
}
