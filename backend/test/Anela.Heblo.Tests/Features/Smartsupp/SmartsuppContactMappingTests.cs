using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppContactMappingTests
{
    // Regression guard for the "Messenger chats missing" incident.
    //
    // When a conversation webhook references a contact we haven't stored yet, the contact is
    // fetched via REST and staged through MapContactDataToEntity -> UpsertContactAsync. That
    // upsert uses ExecuteSqlInterpolated, which types a bare DateTime as `timestamp with time
    // zone`; Npgsql then rejects DateTimeKind.Unspecified values ("Cannot write DateTime with
    // Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"). The staged timestamps
    // used to be stamped Unspecified, so the enclosing conversation upsert threw and the whole
    // conversation was dropped. Facebook Messenger conversations hit this on nearly every event
    // because their Facebook contacts are fetched on demand rather than pre-seeded via contact.*
    // webhooks. Timestamps must be Utc, matching the webhook path (SmartsuppPayloadMapper.MapContact).
    [Fact]
    public void MapContactDataToEntity_StampsAllTimestampsAsUtc()
    {
        // Arrange — mirror the adapter, which returns Unspecified-kind timestamps.
        var data = new SmartsuppContactData
        {
            Id = "ct-messenger-1",
            Name = "Messenger User",
            CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 0, 0), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 5, 0), DateTimeKind.Unspecified),
            BannedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 1, 0), DateTimeKind.Unspecified),
        };
        var syncedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 10, 0), DateTimeKind.Unspecified);

        // Act
        var entity = SmartsuppRepository.MapContactDataToEntity(data, syncedAt);

        // Assert — every timestamp must be Utc so Npgsql accepts the raw-SQL parameters...
        entity.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.SyncedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.BannedAt.Should().NotBeNull();
        entity.BannedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);

        // ...and the wall-clock value must be preserved (relabel, not shift).
        entity.CreatedAt.Should().Be(new DateTime(2026, 7, 13, 6, 0, 0, DateTimeKind.Utc));
        entity.UpdatedAt.Should().Be(new DateTime(2026, 7, 13, 6, 5, 0, DateTimeKind.Utc));
        entity.SyncedAt.Should().Be(new DateTime(2026, 7, 13, 6, 10, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MapContactDataToEntity_NullBannedAt_StaysNull()
    {
        // Arrange
        var data = new SmartsuppContactData
        {
            Id = "ct-messenger-2",
            CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 0, 0), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 0, 0), DateTimeKind.Unspecified),
            BannedAt = null,
        };

        // Act
        var entity = SmartsuppRepository.MapContactDataToEntity(
            data, DateTime.SpecifyKind(new DateTime(2026, 7, 13, 6, 10, 0), DateTimeKind.Unspecified));

        // Assert
        entity.BannedAt.Should().BeNull();
    }
}
