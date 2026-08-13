using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        var repo = new SmartsuppRepository(db, NullLogger<SmartsuppRepository>.Instance);

        // Act
        var ids = await repo.ListOrphanContactConversationIdsAsync(CancellationToken.None);

        // Assert
        ids.Should().BeEquivalentTo(new[] { "c-orphan" });
    }
}
