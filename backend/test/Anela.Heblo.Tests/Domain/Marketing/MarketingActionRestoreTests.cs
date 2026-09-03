using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionRestoreTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction CreateDeletedAction()
        {
            var action = new MarketingActionTestBuilder()
                .WithTitle("Test Action")
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();
            action.SoftDelete("system", "Outlook sync", FixedUtcNow);
            return action;
        }

        [Fact]
        public void Restore_ClearsAllDeletionFields()
        {
            // Arrange
            var action = CreateDeletedAction();
            var restoredAt = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.Restore("user-7", "Restorer", restoredAt);

            // Assert
            action.IsDeleted.Should().BeFalse();
            action.DeletedAt.Should().BeNull();
            action.DeletedByUserId.Should().BeNull();
            action.DeletedByUsername.Should().BeNull();
        }

        [Fact]
        public void Restore_StampsModifiedFieldsWithActorAndUtcNow()
        {
            // Arrange
            var action = CreateDeletedAction();
            var restoredAt = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.Restore("user-7", "Restorer", restoredAt);

            // Assert
            action.ModifiedAt.Should().Be(restoredAt);
            action.ModifiedByUserId.Should().Be("user-7");
            action.ModifiedByUsername.Should().Be("Restorer");
        }
    }
}
