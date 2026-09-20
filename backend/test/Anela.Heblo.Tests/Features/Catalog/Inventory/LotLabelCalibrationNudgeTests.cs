using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

/// <summary>
/// The nudge is the arithmetic behind the operator-facing calibration wizard: the operator
/// reports which way the text drifts and how fast, and the stored pitch/drift pair is moved
/// accordingly. Pitch and drift together are a single value — the effective pitch in
/// hundredths of a dot — so every case here is expressed against that.
/// </summary>
public class LotLabelCalibrationNudgeTests
{
    private const string User = "operator";

    private static LotLabelCalibration Calibration(int pitchDots, int driftDotsPer100Labels) =>
        new(pitchDots, driftDotsPer100Labels, User);

    [Fact]
    public void EffectivePitchHundredths_CombinesPitchAndDrift()
    {
        // The shipped default of 148 dots plus 30 dots per 100 labels is 148.30 dots per label.
        Calibration(148, 30).EffectivePitchHundredths.Should().Be(14830);
    }

    [Theory]
    // Text drifting down means the media advances too slowly, so the gap has to grow.
    [InlineData(LabelDriftDirection.Down, LabelDriftSpeed.Fast, 148, 60)]
    [InlineData(LabelDriftDirection.Down, LabelDriftSpeed.Slow, 148, 45)]
    // Text drifting up means it advances too far, so the gap shrinks.
    [InlineData(LabelDriftDirection.Up, LabelDriftSpeed.Fast, 148, 0)]
    [InlineData(LabelDriftDirection.Up, LabelDriftSpeed.Slow, 148, 15)]
    public void Nudge_MovesEffectivePitch_ByDirectionAndSpeed(
        LabelDriftDirection direction,
        LabelDriftSpeed speed,
        int expectedPitchDots,
        int expectedDriftDotsPer100Labels)
    {
        var result = Calibration(148, 30).Nudge(direction, speed, User);

        result.PitchDots.Should().Be(expectedPitchDots);
        result.DriftDotsPer100Labels.Should().Be(expectedDriftDotsPer100Labels);
    }

    [Fact]
    public void Nudge_CarriesIntoPitch_WhenTheFineGapWouldExceedItsRange()
    {
        // 100 fine points make one whole dot of gap: 148 + 80 fine, plus 30, is not
        // "148 + 110" but 149 + 10.
        var result = Calibration(148, 80).Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, User);

        result.EffectivePitchHundredths.Should().Be(14910);
        result.PitchDots.Should().Be(149);
        result.DriftDotsPer100Labels.Should().Be(10);
    }

    [Fact]
    public void Nudge_BorrowsFromPitch_WhenTheCorrectionExceedsTheFineGap()
    {
        // The same carry downwards: 148 + 10 fine, minus 30, is 147 + 80.
        var result = Calibration(148, 10).Nudge(LabelDriftDirection.Up, LabelDriftSpeed.Fast, User);

        result.EffectivePitchHundredths.Should().Be(14780);
        result.PitchDots.Should().Be(147);
        result.DriftDotsPer100Labels.Should().Be(80);
    }

    [Fact]
    public void Nudge_NormalizesADriftLargerThanOneDot_WithoutChangingTheFeedItRepresents()
    {
        // A drift of 300 per 100 labels is 3 whole dots per label: 148 + 3.00 = 151.00.
        // Nudging folds those whole dots into the pitch, which is the same physical feed.
        var result = Calibration(148, 300).Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Slow, User);

        result.EffectivePitchHundredths.Should().Be(15115);
        result.PitchDots.Should().Be(151);
        result.DriftDotsPer100Labels.Should().Be(15);
    }

    [Fact]
    public void Nudge_ClampsAtTheMaximumPitch()
    {
        // 400 + 80 fine, growing by 30, would overrun the top of the range.
        var result = Calibration(LotLabelCalibration.MaxPitchDots, 80)
            .Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, User);

        result.PitchDots.Should().Be(LotLabelCalibration.MaxPitchDots);
        result.EffectivePitchHundredths.Should()
            .Be(LotLabelCalibration.MaxEffectivePitchHundredths);
    }

    [Fact]
    public void Nudge_NeverMovesTheOppositeWayFromWhatWasReported()
    {
        // The advanced form allows a whole-dot drift, so it can save a calibration above
        // anything the wizard can reach. Clamping such a value must not shrink the gap
        // when the operator reported that the text still drifts down, which asks for a
        // bigger one.
        var aboveTheWizardsRange = Calibration(
            LotLabelCalibration.MaxPitchDots, LotLabelCalibration.MaxDriftDotsPer100Labels);

        var result = aboveTheWizardsRange.Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, User);

        result.EffectivePitchHundredths.Should()
            .BeGreaterThanOrEqualTo(aboveTheWizardsRange.EffectivePitchHundredths);
    }

    [Theory]
    [InlineData(LabelDriftDirection.Up, LabelDriftSpeed.Fast)]
    [InlineData(LabelDriftDirection.Up, LabelDriftSpeed.Slow)]
    [InlineData(LabelDriftDirection.Down, LabelDriftSpeed.Fast)]
    [InlineData(LabelDriftDirection.Down, LabelDriftSpeed.Slow)]
    public void Nudge_HoldsStill_WhenAlreadyAtTheLimitInThatDirection(
        LabelDriftDirection direction, LabelDriftSpeed speed)
    {
        // Callers detect "nothing moved" by comparing the effective pitch, which is how the
        // operator gets told they have hit a limit instead of being invited to print
        // another batch and click forever.
        var atTheLimit = direction == LabelDriftDirection.Down
            ? Calibration(LotLabelCalibration.MaxPitchDots, 99)
            : Calibration(LotLabelCalibration.MinPitchDots, 0);

        var result = atTheLimit.Nudge(direction, speed, User);

        result.EffectivePitchHundredths.Should().Be(atTheLimit.EffectivePitchHundredths);
        result.PitchDots.Should().Be(atTheLimit.PitchDots);
        result.DriftDotsPer100Labels.Should().Be(atTheLimit.DriftDotsPer100Labels);
    }

    [Fact]
    public void Nudge_HoldsAnOutOfRangeValueExactly_RatherThanRewritingItIntoRange()
    {
        // pitch 400 + drift 1000 is 410 dots per label: saveable through the advanced form,
        // but above anything the wizard can represent. Holding must keep the stored pair
        // intact rather than re-deriving a pitch of 410, which no longer round-trips.
        var beyondTheWizardsRange = Calibration(
            LotLabelCalibration.MaxPitchDots, LotLabelCalibration.MaxDriftDotsPer100Labels);

        var result = beyondTheWizardsRange.Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, User);

        result.PitchDots.Should().Be(LotLabelCalibration.MaxPitchDots);
        result.DriftDotsPer100Labels.Should().Be(LotLabelCalibration.MaxDriftDotsPer100Labels);
    }

    [Theory]
    [InlineData(LabelDriftSpeed.Fast, 970)]
    [InlineData(LabelDriftSpeed.Slow, 985)]
    public void Nudge_StepsAnOutOfRangeValueByOneNudge_RatherThanSnappingToTheCeiling(
        LabelDriftSpeed speed, int expectedDriftDotsPer100Labels)
    {
        // pitch 400 + drift 1000 is 410 dots per label, nine dots above the wizard's ceiling.
        // Reporting "up" asks for a hair less gap. Clamping to the ceiling would instead cut
        // nine whole dots in one click, so the drift steps by exactly one nudge and the
        // pitch stays where the advanced form put it.
        var beyondTheWizardsRange = Calibration(
            LotLabelCalibration.MaxPitchDots, LotLabelCalibration.MaxDriftDotsPer100Labels);

        var result = beyondTheWizardsRange.Nudge(LabelDriftDirection.Up, speed, User);

        result.PitchDots.Should().Be(LotLabelCalibration.MaxPitchDots);
        result.DriftDotsPer100Labels.Should().Be(expectedDriftDotsPer100Labels);
    }

    [Fact]
    public void Nudge_ReturnsToTheWizardsRange_OneStepAtATime()
    {
        // Just above the ceiling the single step lands inside the range and reads as a
        // normal in-range pair, the same result the plain arithmetic would give.
        var result = Calibration(LotLabelCalibration.MaxPitchDots, 120)
            .Nudge(LabelDriftDirection.Up, LabelDriftSpeed.Fast, User);

        result.PitchDots.Should().Be(LotLabelCalibration.MaxPitchDots);
        result.DriftDotsPer100Labels.Should().Be(90);
    }

    [Fact]
    public void Nudge_ClampsAtTheMinimumPitch()
    {
        var result = Calibration(LotLabelCalibration.MinPitchDots, 0)
            .Nudge(LabelDriftDirection.Up, LabelDriftSpeed.Fast, User);

        result.PitchDots.Should().Be(LotLabelCalibration.MinPitchDots);
        result.DriftDotsPer100Labels.Should().Be(0);
    }

    [Fact]
    public void Nudge_AlwaysProducesValuesWithinThePersistedRange()
    {
        // Every reachable result must satisfy the same bounds the set-calibration validator
        // enforces, because the nudge endpoint writes without going through that validator.
        var directions = new[] { LabelDriftDirection.Up, LabelDriftDirection.Down };
        var speeds = new[] { LabelDriftSpeed.Fast, LabelDriftSpeed.Slow };

        for (var pitch = LotLabelCalibration.MinPitchDots; pitch <= LotLabelCalibration.MaxPitchDots; pitch++)
        {
            // Includes the drift values above 99 that only the advanced form can save, which
            // is where a naive clamp stops being able to express the result.
            foreach (var drift in new[] { 0, 1, 50, 99, 100, 250, LotLabelCalibration.MaxDriftDotsPer100Labels })
            {
                foreach (var direction in directions)
                {
                    foreach (var speed in speeds)
                    {
                        var result = Calibration(pitch, drift).Nudge(direction, speed, User);

                        result.PitchDots.Should().BeInRange(
                            LotLabelCalibration.MinPitchDots, LotLabelCalibration.MaxPitchDots);
                        result.DriftDotsPer100Labels.Should().BeInRange(
                            LotLabelCalibration.MinDriftDotsPer100Labels,
                            LotLabelCalibration.MaxDriftDotsPer100Labels);
                    }
                }
            }
        }
    }

    [Fact]
    public void Nudge_ReturnsANewInstance_AndLeavesTheOriginalUntouched()
    {
        var original = Calibration(148, 30);

        var result = original.Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, "someone-else");

        result.Should().NotBeSameAs(original);
        original.PitchDots.Should().Be(148);
        original.DriftDotsPer100Labels.Should().Be(30);
        result.ModifiedBy.Should().Be("someone-else");
    }

    [Fact]
    public void Nudge_IsReversible_WhenTheSameSpeedIsAppliedInBothDirections()
    {
        var original = Calibration(148, 30);

        var thereAndBack = original
            .Nudge(LabelDriftDirection.Down, LabelDriftSpeed.Fast, User)
            .Nudge(LabelDriftDirection.Up, LabelDriftSpeed.Fast, User);

        thereAndBack.EffectivePitchHundredths.Should().Be(original.EffectivePitchHundredths);
    }
}
