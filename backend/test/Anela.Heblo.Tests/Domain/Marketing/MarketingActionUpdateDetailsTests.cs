using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionUpdateDetailsTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

        private static MarketingAction NewAction() =>
            new MarketingActionTestBuilder().WithTitle("seed").Build();

        [Fact]
        public void UpdateDetails_TrimsLeadingAndTrailingWhitespaceFromTitle()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: "  Spring Launch  ",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: UtcNow);

            action.Title.Should().Be("Spring Launch");
        }

        [Fact]
        public void UpdateDetails_ReplacesNullTitleWithEmptyString()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: null!,
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: null,
                utcNow: UtcNow);

            action.Title.Should().Be(string.Empty);
        }

        [Fact]
        public void UpdateDetails_PreservesNullDescription()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: UtcNow);

            action.Description.Should().BeNull();
        }

        [Fact]
        public void UpdateDetails_TrimsDescriptionWhenPresent()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: "Title",
                description: "  body  ",
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: UtcNow);

            action.Description.Should().Be("body");
        }

        [Fact]
        public void UpdateDetails_DefaultsModifiedByUsernameToUnknownUserWhenNull()
        {
            var action = NewAction();

            action.UpdateDetails(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: null,
                utcNow: UtcNow);

            action.ModifiedByUsername.Should().Be("Unknown User");
        }

        [Fact]
        public void UpdateDetails_SetsModifiedAtToProvidedUtcNow()
        {
            var action = NewAction();
            var moment = new DateTime(2026, 7, 4, 9, 30, 0, DateTimeKind.Utc);

            action.UpdateDetails(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                modifiedByUserId: "user-1",
                modifiedByUsername: "alice",
                utcNow: moment);

            action.ModifiedAt.Should().Be(moment);
        }

        [Fact]
        public void UpdateDetails_AssignsAllOtherScalarFieldsExactlyAsPassed()
        {
            var action = NewAction();
            var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);

            action.UpdateDetails(
                title: "Title",
                description: "Desc",
                actionType: MarketingActionType.PR,
                startDate: start,
                endDate: end,
                modifiedByUserId: "user-42",
                modifiedByUsername: "bob",
                utcNow: UtcNow);

            action.ActionType.Should().Be(MarketingActionType.PR);
            action.StartDate.Should().Be(start);
            action.EndDate.Should().Be(end);
            action.ModifiedByUserId.Should().Be("user-42");
            action.ModifiedByUsername.Should().Be("bob");
        }
    }
}
