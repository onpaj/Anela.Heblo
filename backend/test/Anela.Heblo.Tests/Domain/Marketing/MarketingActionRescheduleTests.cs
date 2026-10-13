using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionRescheduleTests
    {
        private static readonly DateTime FixedUtcNow =
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        private static MarketingAction NewAction() =>
            new MarketingActionTestBuilder()
                .WithTitle("Original Title")
                .WithDescription("Original Description")
                .WithActionType(MarketingActionType.Blog)
                .WithStartDate(FixedUtcNow)
                .WithCreatedAt(FixedUtcNow)
                .WithModifiedAt(FixedUtcNow)
                .WithCreatedBy("user-1")
                .Build();

        [Fact]
        public void Reschedule_UpdatesStartAndEndDateToPassedInValues()
        {
            var action = NewAction();
            var newStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var newEnd = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: newStart,
                endDate: newEnd,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.StartDate.Should().Be(newStart);
            action.EndDate.Should().Be(newEnd);
        }

        [Fact]
        public void Reschedule_AllowsNullEndDate()
        {
            var action = NewAction();
            var newStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: newStart,
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.StartDate.Should().Be(newStart);
            action.EndDate.Should().BeNull();
        }

        [Fact]
        public void Reschedule_LeavesTitleDescriptionAndActionTypeUnchanged()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.Title.Should().Be("Original Title");
            action.Description.Should().Be("Original Description");
            action.ActionType.Should().Be(MarketingActionType.Blog);
        }

        [Fact]
        public void Reschedule_SetsModifiedAtToProvidedUtcNow()
        {
            var action = NewAction();
            var moment = new DateTime(2026, 7, 4, 9, 30, 0, DateTimeKind.Utc);

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: moment);

            action.ModifiedAt.Should().Be(moment);
        }

        [Fact]
        public void Reschedule_SetsModifiedByUserIdAndUsernameFromArguments()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-42",
                modifiedByUsername: "bob",
                utcNow: FixedUtcNow);

            action.ModifiedByUserId.Should().Be("user-42");
            action.ModifiedByUsername.Should().Be("bob");
        }

        [Fact]
        public void Reschedule_DefaultsModifiedByUsernameToUnknownUserWhenNull()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-42",
                modifiedByUsername: null,
                utcNow: FixedUtcNow);

            action.ModifiedByUsername.Should().Be("Unknown User");
        }

        [Fact]
        public void Reschedule_DoesNotModifyCreatedAuditFields()
        {
            var action = NewAction();
            var originalCreatedAt = action.CreatedAt;
            var originalCreatedByUserId = action.CreatedByUserId;
            var originalCreatedByUsername = action.CreatedByUsername;

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.CreatedAt.Should().Be(originalCreatedAt);
            action.CreatedByUserId.Should().Be(originalCreatedByUserId);
            action.CreatedByUsername.Should().Be(originalCreatedByUsername);
        }

        [Fact]
        public void Reschedule_DoesNotModifyDeletionOrOutlookFields()
        {
            var action = NewAction();

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.IsDeleted.Should().BeFalse();
            action.DeletedAt.Should().BeNull();
            action.OutlookEventId.Should().BeNull();
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.NotSynced);
        }

        [Fact]
        public void Reschedule_DoesNotModifyProductAssociationsOrFolderLinks()
        {
            var action = NewAction();
            action.AssociateWithProduct("PROD-1", FixedUtcNow);
            action.LinkToFolder("folder-key", MarketingFolderType.General, FixedUtcNow);

            action.Reschedule(
                startDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                endDate: null,
                modifiedByUserId: "user-7",
                modifiedByUsername: "Mover",
                utcNow: FixedUtcNow);

            action.ProductAssociations.Should().ContainSingle(p => p.ProductCodePrefix == "PROD-1");
            action.FolderLinks.Should().ContainSingle(f => f.FolderKey == "folder-key");
        }
    }
}
