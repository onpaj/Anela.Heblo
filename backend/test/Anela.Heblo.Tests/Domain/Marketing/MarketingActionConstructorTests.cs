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
                createdByUserId: "user-42",
                createdByUsername: "bob",
                utcNow: UtcNow);

            action.ActionType.Should().Be(MarketingActionType.PR);
            action.StartDate.Should().Be(start);
            action.EndDate.Should().Be(end);
            action.CreatedByUserId.Should().Be("user-42");
        }
    }
}
