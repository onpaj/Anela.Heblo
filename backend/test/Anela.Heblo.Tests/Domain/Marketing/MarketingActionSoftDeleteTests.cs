using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionSoftDeleteTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithTitle("Test Action")
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void SoftDelete_AssignsDeletedAtAndModifiedAtFromUtcNowParameter_Exactly()
        {
            // Arrange
            var action = CreateAction();
            var fixedNow = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.SoftDelete("user-7", "Test User", fixedNow);

            // Assert
            action.DeletedAt.Should().Be(fixedNow);
            action.ModifiedAt.Should().Be(fixedNow);
        }

        [Fact]
        public void SoftDelete_DeletedAtEqualsModifiedAt_NoMillisecondDriftBetweenFields()
        {
            // Arrange
            var action = CreateAction();
            var fixedNow = new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc);

            // Act
            action.SoftDelete("user-7", "Test User", fixedNow);

            // Assert
            action.DeletedAt.Should().Be(action.ModifiedAt);
        }

        [Fact]
        public void SoftDelete_PopulatesAuditFields()
        {
            // Arrange
            var action = CreateAction();

            // Act
            action.SoftDelete("user-7", "Test User", FixedUtcNow);

            // Assert
            action.IsDeleted.Should().BeTrue();
            action.DeletedByUserId.Should().Be("user-7");
            action.DeletedByUsername.Should().Be("Test User");
            action.ModifiedByUserId.Should().Be("user-7");
            action.ModifiedByUsername.Should().Be("Test User");
        }
    }
}
