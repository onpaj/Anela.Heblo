using System;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingActionConstructorTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Ctor_TrimsTitleWhitespace()
        {
            var action = new MarketingAction(
                title: "  Hello  ",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: UtcNow);

            action.Title.Should().Be("Hello");
        }

        [Fact]
        public void Ctor_TrimsDescriptionWhenPresent()
        {
            var action = new MarketingAction(
                title: "Title",
                description: "  body  ",
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: UtcNow);

            action.Description.Should().Be("body");
        }

        [Fact]
        public void Ctor_PreservesNullDescription()
        {
            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: null,
                utcNow: UtcNow);

            action.Description.Should().BeNull();
        }

        [Fact]
        public void Ctor_DefaultsCreatedByUsernameToUnknownUserWhenNull()
        {
            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: null,
                utcNow: UtcNow);

            action.CreatedByUsername.Should().Be("Unknown User");
        }

        [Fact]
        public void Ctor_SetsCreatedAtAndModifiedAtToUtcNow()
        {
            var moment = new DateTime(2026, 7, 4, 9, 30, 0, DateTimeKind.Utc);

            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: false,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: moment);

            action.CreatedAt.Should().Be(moment);
            action.ModifiedAt.Should().Be(moment);
        }

        [Fact]
        public void Ctor_AssignsRemainingScalarsExactlyAsPassed()
        {
            var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);

            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.PR,
                startDate: start,
                endDate: end,
                isAllDay: false,
                createdByUserId: "user-42",
                createdByUsername: "bob",
                utcNow: UtcNow);

            action.ActionType.Should().Be(MarketingActionType.PR);
            action.StartDate.Should().Be(start);
            action.EndDate.Should().Be(end);
            action.CreatedByUserId.Should().Be("user-42");
        }

        [Fact]
        public void Ctor_SetsIsAllDayExactlyAsPassed()
        {
            var action = new MarketingAction(
                title: "Title",
                description: null,
                actionType: MarketingActionType.Newsletter,
                startDate: UtcNow,
                endDate: null,
                isAllDay: true,
                createdByUserId: "user-1",
                createdByUsername: "alice",
                utcNow: UtcNow);

            action.IsAllDay.Should().BeTrue();
        }

        [Theory]
        [InlineData("2026-09-18T00:00:00", "2026-09-19T00:00:00", true)]   // midnight to midnight
        [InlineData("2026-09-01T07:00:00", "2026-09-01T08:30:00", false)]  // timed same-day
        [InlineData("2026-09-18T00:00:00", null, false)]                  // midnight start, no end
        public void ComputeIsAllDay_MatchesTheMidnightToMidnightRule(
            string startText, string? endText, bool expected)
        {
            var start = DateTime.Parse(startText, null, System.Globalization.DateTimeStyles.RoundtripKind);
            DateTime? end = endText is null
                ? null
                : DateTime.Parse(endText, null, System.Globalization.DateTimeStyles.RoundtripKind);

            MarketingAction.ComputeIsAllDay(start, end).Should().Be(expected);
        }
    }
}
