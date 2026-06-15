using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppRepositoryContactConversationsTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SmartsuppRepository NewRepo(ApplicationDbContext db) =>
        SmartsuppRepositoryTestFactory.New(db);

    private static SmartsuppConversation MakeConversation(string id, string? contactId, DateTime lastMessageAt) =>
        new()
        {
            Id = id,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            LastMessageAt = DateTime.SpecifyKind(lastMessageAt, DateTimeKind.Unspecified),
            CreatedAt = DateTime.SpecifyKind(lastMessageAt.AddHours(-1), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(lastMessageAt, DateTimeKind.Unspecified),
            SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        };

    [Fact]
    public async Task ListConversationsForContactAsync_ReturnsSiblingsOrderedByLastMessageAtDesc()
    {
        // Arrange
        await using var db = NewContext();
        var older = MakeConversation("c-older", "contact-1", new DateTime(2026, 5, 1, 10, 0, 0));
        var newer = MakeConversation("c-newer", "contact-1", new DateTime(2026, 5, 3, 10, 0, 0));
        var current = MakeConversation("c-current", "contact-1", new DateTime(2026, 5, 2, 10, 0, 0));
        db.SmartsuppConversations.AddRange(older, newer, current);
        await db.SaveChangesAsync();

        var repo = NewRepo(db);

        // Act
        var result = await repo.ListConversationsForContactAsync("contact-1", "c-current", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("c-newer");
        result[1].Id.Should().Be("c-older");
    }

    [Fact]
    public async Task ListConversationsForContactAsync_ExcludesCurrentConversation()
    {
        // Arrange
        await using var db = NewContext();
        var sibling = MakeConversation("c-sibling", "contact-1", new DateTime(2026, 5, 1, 10, 0, 0));
        var current = MakeConversation("c-current", "contact-1", new DateTime(2026, 5, 2, 10, 0, 0));
        db.SmartsuppConversations.AddRange(sibling, current);
        await db.SaveChangesAsync();

        var repo = NewRepo(db);

        // Act
        var result = await repo.ListConversationsForContactAsync("contact-1", "c-current", CancellationToken.None);

        // Assert
        result.Should().ContainSingle(c => c.Id == "c-sibling");
        result.Should().NotContain(c => c.Id == "c-current");
    }

    [Fact]
    public async Task ListConversationsForContactAsync_DoesNotReturnOtherContactsConversations()
    {
        // Arrange
        await using var db = NewContext();
        var mine = MakeConversation("c-mine", "contact-1", new DateTime(2026, 5, 1, 10, 0, 0));
        var theirs = MakeConversation("c-theirs", "contact-2", new DateTime(2026, 5, 1, 10, 0, 0));
        var current = MakeConversation("c-current", "contact-1", new DateTime(2026, 5, 2, 10, 0, 0));
        db.SmartsuppConversations.AddRange(mine, theirs, current);
        await db.SaveChangesAsync();

        var repo = NewRepo(db);

        // Act
        var result = await repo.ListConversationsForContactAsync("contact-1", "c-current", CancellationToken.None);

        // Assert
        result.Should().ContainSingle(c => c.Id == "c-mine");
    }

    [Fact]
    public async Task ListConversationsForContactAsync_LimitsResultsTo20()
    {
        // Arrange
        await using var db = NewContext();
        var conversations = Enumerable.Range(1, 25)
            .Select(i => MakeConversation($"c-{i}", "contact-1", new DateTime(2026, 1, i, 10, 0, 0)))
            .ToList();
        db.SmartsuppConversations.AddRange(conversations);
        await db.SaveChangesAsync();

        var repo = NewRepo(db);

        // Act — "c-none" doesn't exist, so nothing is excluded
        var result = await repo.ListConversationsForContactAsync("contact-1", "c-none", CancellationToken.None);

        // Assert
        result.Should().HaveCount(20);
    }
}
