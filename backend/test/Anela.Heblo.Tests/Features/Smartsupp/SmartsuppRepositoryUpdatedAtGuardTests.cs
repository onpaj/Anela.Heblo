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

public class SmartsuppRepositoryUpdatedAtGuardTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppConversation MakeConversation(string id, DateTime updatedAt, string? subject = null) =>
        new()
        {
            Id = id,
            Status = SmartsuppConversationStatus.Open,
            Subject = subject,
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    [Fact]
    public async Task UpsertConversationAsync_AppliesUpdate_WhenIncomingIsNewer()
    {
        // Arrange
        await using var db = NewContext();
        var existing = MakeConversation("c1", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified), subject: "old");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeConversation("c1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified), subject: "new");

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.UpdatedAt.Should().Be(incoming.UpdatedAt);
    }

    [Fact]
    public async Task UpsertConversationAsync_SkipsUpdate_WhenIncomingIsOlder()
    {
        // Arrange
        await using var db = NewContext();
        var existingUpdatedAt = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Unspecified);
        var existing = MakeConversation("c1", existingUpdatedAt, subject: "newer");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var staleIncoming = MakeConversation("c1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified), subject: "older");

        // Act — should be a no-op, must not throw
        await repo.UpsertConversationAsync(staleIncoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — UpdatedAt unchanged
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.UpdatedAt.Should().Be(existingUpdatedAt);
    }

    [Fact]
    public async Task UpsertConversationAsync_InsertsNew_WhenIdNotPresent()
    {
        // Arrange
        await using var db = NewContext();
        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeConversation("c-new", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.Id.Should().Be("c-new");
    }
}

public class SmartsuppRepositoryContactGuardTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppContact MakeContact(string id, DateTime updatedAt) =>
        new()
        {
            Id = id,
            Email = "test@example.com",
            CreatedAt = DateTime.SpecifyKind(updatedAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    [Fact]
    public async Task UpsertContactAsync_AppliesUpdate_WhenIncomingIsNewer()
    {
        await using var db = NewContext();
        var existing = MakeContact("ct1", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified));
        db.SmartsuppContacts.Add(existing);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeContact("ct1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified));
        incoming.Email = "updated@example.com";

        await repo.UpsertContactAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.SmartsuppContacts.SingleAsync();
        stored.Email.Should().Be("updated@example.com");
    }

    [Fact]
    public async Task UpsertContactAsync_SkipsUpdate_WhenIncomingIsOlder()
    {
        await using var db = NewContext();
        var existingUpdatedAt = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Unspecified);
        var existing = MakeContact("ct1", existingUpdatedAt);
        existing.Email = "original@example.com";
        db.SmartsuppContacts.Add(existing);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var stale = MakeContact("ct1", new DateTime(2026, 5, 13, 11, 0, 0, DateTimeKind.Unspecified));
        stale.Email = "stale@example.com";

        await repo.UpsertContactAsync(stale, CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.SmartsuppContacts.SingleAsync();
        stored.Email.Should().Be("original@example.com");
    }

    [Fact]
    public async Task UpsertContactAsync_InsertsNew_WhenIdNotPresent()
    {
        await using var db = NewContext();
        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeContact("ct-new", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified));

        await repo.UpsertContactAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.SmartsuppContacts.SingleAsync();
        stored.Id.Should().Be("ct-new");
    }
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

    [Fact]
    public async Task UpsertConversationAsync_PreservesDenormFields_WhenNewValuesAreNull()
    {
        // Arrange
        await using var db = NewContext();
        var existing = MakeConversation("c1", contactName: "Jana Nováková", contactEmail: "jana@x.cz");
        db.SmartsuppConversations.Add(existing);
        await db.SaveChangesAsync();

        var repo = SmartsuppRepositoryTestFactory.New(db);
        var incoming = MakeConversation("c1", contactName: null, contactEmail: null);
        incoming.UpdatedAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Jana Nováková");
        stored.ContactEmail.Should().Be("jana@x.cz");
    }

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

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.ContactName.Should().Be("Petr Novák");
        stored.ContactEmail.Should().Be("petr@x.cz");
        stored.ContactId.Should().Be("c-contact-1");
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
