using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

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

        var repo = new SmartsuppRepository(db);
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

        var repo = new SmartsuppRepository(db);
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
        var repo = new SmartsuppRepository(db);
        var incoming = MakeConversation("c-new", new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Unspecified));

        // Act
        await repo.UpsertConversationAsync(incoming, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert
        var stored = await db.SmartsuppConversations.SingleAsync();
        stored.Id.Should().Be("c-new");
    }
}
