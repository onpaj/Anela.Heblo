using Anela.Heblo.Application.Features.Attendance.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Attendance;

public class BreakSlotCalculatorTests
{
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(30);

    private static TimeSlot Slot(int fromHour, int fromMin, int toHour, int toMin) => new(
        new DateTime(2026, 8, 3, fromHour, fromMin, 0),
        new DateTime(2026, 8, 3, toHour, toMin, 0));

    private static readonly TimeSlot Preferred = Slot(11, 0, 11, 30);

    [Fact]
    public void ReturnsPreferredWindow_WhenFullyInsideAWorkSegment()
    {
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(8, 0, 16, 30) }, Preferred, BreakDuration);

        slot.Should().Be(Preferred);
    }

    [Fact]
    public void FallsBackToMidpoint_WhenWorkStartsInsidePreferredWindow()
    {
        // Work 11:15–19:15 (8h) — preferred window is not fully inside.
        // Center: 11:15 + (8:00 − 0:30)/2 = 11:15 + 3:45 = 15:00 → 15:00–15:30.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(11, 15, 19, 15) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(15, 0, 15, 30));
    }

    [Fact]
    public void FallsBackToMidpoint_ForAfternoonShift()
    {
        // Work 13:00–20:00 (7h) — center: 16:15–16:45.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(13, 0, 20, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(16, 15, 16, 45));
    }

    [Fact]
    public void PreferredWindowTouchingSegmentStart_DoesNotCount_BreakMustInterrupt()
    {
        // Work starts exactly at 11:00 — a break at 11:00 would sit at the shift edge,
        // not interrupt it. Center of 11:00–19:00 → 14:45–15:15.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(11, 0, 19, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(14, 45, 15, 15));
    }

    [Fact]
    public void PicksLongestSegment_WhenMultipleSegmentsExist()
    {
        // 6:00–8:00 (2h) and 9:00–14:00 (5h): longest is 9:00–14:00, center 11:15–11:45.
        // Preferred 11:00–11:30 IS inside 9:00–14:00, so preferred wins.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(6, 0, 8, 0), Slot(9, 0, 14, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Preferred);
    }

    [Fact]
    public void PicksLongestSegment_WhenPreferredWindowIsInNoSegment()
    {
        // 6:00–10:00 (4h) and 12:00–18:00 (6h): preferred 11:00–11:30 in a gap.
        // Longest 12:00–18:00 → center 14:45–15:15.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(6, 0, 10, 0), Slot(12, 0, 18, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(14, 45, 15, 15));
    }

    [Fact]
    public void RoundsMidpointToNearestFiveMinutes()
    {
        // Work 8:07–16:33 → duration 8:26, center start = 8:07 + 3:58 = 12:05 (already rounds cleanly);
        // use 8:06–16:33 → center start 12:04:30 → rounds to 12:05.
        // Preferred window must lie outside the segment here, otherwise the preferred-window
        // branch would short-circuit before the rounding logic under test ever runs
        // (11:00–11:30 sits strictly inside 8:06–16:33).
        var preferredOutsideSegment = Slot(6, 0, 6, 30);
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(8, 6, 16, 33) }, preferredOutsideSegment, BreakDuration);

        slot!.Start.Minute.Should().Be(5);
        slot.Start.Hour.Should().Be(12);
    }

    [Fact]
    public void ReturnsNull_WhenNoSegmentCanFitTheBreakWithMargins()
    {
        // Longest segment 35 min < 30 min break + 5 min margin each side.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(9, 0, 9, 35) }, Preferred, BreakDuration);

        slot.Should().BeNull();
    }

    [Fact]
    public void ReturnsNull_ForEmptySegments()
    {
        BreakSlotCalculator.ComputeBreakSlot(
            Array.Empty<TimeSlot>(), Preferred, BreakDuration).Should().BeNull();
    }

    [Fact]
    public void BuildSegments_MergesOverlappingAndAdjacentIntervals()
    {
        var segments = BreakSlotCalculator.BuildSegments(new[]
        {
            Slot(8, 0, 12, 0),
            Slot(12, 0, 14, 0),   // adjacent — merges
            Slot(13, 30, 15, 0),  // overlapping — merges
            Slot(16, 0, 17, 0)    // separate
        });

        segments.Should().HaveCount(2);
        segments[0].Should().Be(Slot(8, 0, 15, 0));
        segments[1].Should().Be(Slot(16, 0, 17, 0));
    }
}
