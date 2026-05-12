using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionSyncTests
    {
        private static MarketingAction CreateAction()
        {
            return new MarketingAction
            {
                Title = "Test Action",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedByUserId = "user-1",
            };
        }

        [Fact]
        public void MarkOutlookSynced_SetsEventIdAndStatus_WhenCalled()
        {
            // Arrange
            var action = CreateAction();
            var eventId = "AAMkAGI2NGVhZTVlLTI1OGMtNDI4My1hZTZmLWRmYjE=";
            var utcNow = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);

            // Act
            action.MarkOutlookSynced(eventId, utcNow);

            // Assert
            action.OutlookEventId.Should().Be(eventId);
            action.OutlookLastAttemptAt.Should().Be(utcNow);
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
        }

        [Fact]
        public void MarkOutlookSynced_ClearsError_WhenCalled()
        {
            // Arrange
            var action = CreateAction();
            action.OutlookSyncError = "previous error";
            var utcNow = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);

            // Act
            action.MarkOutlookSynced("event-id-123", utcNow);

            // Assert
            action.OutlookSyncError.Should().BeNull();
        }

        [Fact]
        public void MarkOutlookFailed_SetsFailedStatusAndTruncatesError_WhenErrorIsLong()
        {
            // Arrange
            var action = CreateAction();
            var longError = new string('x', 1500);
            var utcNow = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);

            // Act
            action.MarkOutlookFailed(longError, utcNow);

            // Assert
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Failed);
            action.OutlookSyncError.Should().HaveLength(1000);
            action.OutlookSyncError.Should().Be(new string('x', 1000));
        }

        [Fact]
        public void MarkOutlookFailed_KeepsEventId_WhenFailed()
        {
            // Arrange
            var action = CreateAction();
            var existingEventId = "existing-event-id";
            action.OutlookEventId = existingEventId;
            var utcNow = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);

            // Act
            action.MarkOutlookFailed("transient error", utcNow);

            // Assert
            // Event ID must be preserved so retry logic can attempt an update rather than a create
            action.OutlookEventId.Should().Be(existingEventId);
        }

        [Fact]
        public void ClearOutlookLink_ResetsAllSyncFields()
        {
            // Arrange
            var action = CreateAction();
            action.OutlookEventId = "some-event-id";
            action.OutlookLastAttemptAt = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);
            action.OutlookSyncStatus = MarketingSyncStatus.Synced;
            action.OutlookSyncError = "some error";

            // Act
            action.ClearOutlookLink();

            // Assert
            action.OutlookEventId.Should().BeNull();
            action.OutlookLastAttemptAt.Should().BeNull();
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.NotSynced);
            action.OutlookSyncError.Should().BeNull();
        }
    }
}
