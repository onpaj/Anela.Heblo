using System;
using Anela.Heblo.Application.Features.Marketing.Infrastructure;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

/// <summary>
/// Graph reports an event's end as exclusive; Heblo's EndDate is inclusive
/// (the frontend renders dateFrom..dateTo as a closed interval). The gap only
/// shows on all-day events, where the exclusive end is midnight of the
/// following day and the action would render one day too long.
/// </summary>
public class OutlookEventImportMapperTests
{
    private static readonly SyncActor Actor = new("user-import", "Import User");
    private static readonly DateTime UtcNow = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static OutlookEventDto BuildEvent(DateTime start, DateTime end, bool isAllDay)
    {
        return new OutlookEventDto
        {
            Id = "evt-1",
            Subject = "Linda Odyssea",
            Start = new GraphEventDateTime { DateTimeString = start.ToString("O"), TimeZone = "UTC" },
            End = new GraphEventDateTime { DateTimeString = end.ToString("O"), TimeZone = "UTC" },
            IsAllDay = isAllDay,
            Categories = Array.Empty<string>(),
        };
    }

    private static MarketingAction Build(OutlookEventDto evt) =>
        OutlookEventImportMapper.BuildAction(evt, Actor, UtcNow, MarketingActionType.Event);

    [Fact]
    public void BuildAction_ForSingleDayAllDayEvent_EndsOnTheSameDayItStarts()
    {
        // Arrange — Graph shape for an all-day event on 18. 9. 2026
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: true);

        // Act
        var action = Build(evt);

        // Assert
        action.StartDate.Should().Be(new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));
        action.EndDate.Should().Be(new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void BuildAction_ForMultiDayAllDayEvent_EndsOnItsLastDay()
    {
        // Arrange — 7.–9. 9. 2026 in Outlook is start 7. 9., exclusive end 10. 9.
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: true);

        // Act
        var action = Build(evt);

        // Assert
        action.EndDate.Should().Be(new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void BuildAction_ForTimedEvent_KeepsGraphEndUntouched()
    {
        // Arrange — a timed meeting must not lose a day
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            isAllDay: false);

        // Act
        var action = Build(evt);

        // Assert
        action.EndDate.Should().Be(new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void HasChanges_ForAlreadyImportedAllDayEventWithExclusiveEnd_ReportsAChange()
    {
        // Arrange — a row written by the old mapper, so the hourly sync heals it
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: true);

        var existing = new MarketingAction(
            title: "Linda Odyssea",
            description: null,
            actionType: MarketingActionType.Event,
            startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            endDate: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: false,
            createdByUserId: Actor.UserId,
            createdByUsername: Actor.Username,
            utcNow: UtcNow);

        // Act
        var hasChanges = OutlookEventImportMapper.HasChanges(existing, evt, MarketingActionType.Event);

        // Assert
        hasChanges.Should().BeTrue();
    }

    [Fact]
    public void BuildAction_SetsIsAllDayFromGraphsFlag_ForAllDayEvent()
    {
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: true);

        var action = Build(evt);

        action.IsAllDay.Should().BeTrue();
    }

    [Fact]
    public void BuildAction_SetsIsAllDayFromGraphsFlag_ForTimedMidnightToMidnightEvent()
    {
        // This is the exact failure scenario from the issue: a genuinely timed
        // 24-hour event whose dates happen to look like an all-day event must
        // NOT be recorded as all-day, because Graph says isAllDay: false.
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            isAllDay: false);

        var action = Build(evt);

        action.IsAllDay.Should().BeFalse();
        action.EndDate.Should().Be(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void HasChanges_ForToggledIsAllDayWithUnchangedDates_ReportsAChange()
    {
        var evt = BuildEvent(
            start: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            end: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            isAllDay: false);

        var existing = new MarketingAction(
            title: "Linda Odyssea",
            description: null,
            actionType: MarketingActionType.Event,
            startDate: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            isAllDay: true,
            createdByUserId: Actor.UserId,
            createdByUsername: Actor.Username,
            utcNow: UtcNow);

        var hasChanges = OutlookEventImportMapper.HasChanges(existing, evt, MarketingActionType.Event);

        hasChanges.Should().BeTrue();
    }
}
