using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionSyncTests
    {
        private static MarketingAction CreateAction() =>
            new MarketingActionTestBuilder()
                .WithTitle("Test Action")
                .WithStartDate(DateTime.UtcNow)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .Build();

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
