namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// Single-row calibration for the round lot-label media: the vertical pitch (in dots) the
/// printer advances per label in continuous mode, plus a drift correction expressed as the
/// total number of extra dots to spread evenly across every 100 labels. Persisted so an
/// administrator can tune it once for all sessions without a code change.
/// </summary>
public class LotLabelCalibration
{
    public const int DefaultPitchDots = 148;
    public const int MinPitchDots = 80;
    public const int MaxPitchDots = 400;

    // Drift correction: total extra dots spread evenly across every 100 labels. 0 disables it.
    public const int DefaultDriftDotsPer100Labels = 30;
    public const int MinDriftDotsPer100Labels = 0;
    public const int MaxDriftDotsPer100Labels = 1000;

    // Correction applied by one wizard nudge, in hundredths of a dot per label: 30 when the
    // drift shows up within ~20 labels, half that when it takes ~50. Both are deliberately
    // small — a third and a sixth of a single dot — because the printer only ever needs a
    // hair of correction, and the wizard is a self-correcting loop that can be clicked again.
    public const int NudgeFastHundredths = 30;
    public const int NudgeSlowHundredths = 15;

    // The effective pitch the wizard can reach. The ceiling carries a 99 fraction so the
    // whole pitch range stays reachable; a calibration saved through the advanced form can
    // sit above it, because that form allows a drift of a whole dot or more per label.
    public const int MinEffectivePitchHundredths = MinPitchDots * 100;
    public const int MaxEffectivePitchHundredths = MaxPitchDots * 100 + 99;

    public int Id { get; private set; }
    public int PitchDots { get; private set; }
    public int DriftDotsPer100Labels { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    /// <summary>
    /// Pitch and drift are two halves of one quantity: the dots the printer advances per
    /// label, where the drift carries the sub-dot fraction (1 dot = 100 per 100 labels).
    /// Expressing it as a single integer is what lets the wizard reason about a nudge
    /// without exposing either field to the operator.
    /// </summary>
    public int EffectivePitchHundredths => PitchDots * 100 + DriftDotsPer100Labels;

    private LotLabelCalibration() { }

    public static LotLabelCalibration CreateDefault() => new()
    {
        Id = 1,
        PitchDots = DefaultPitchDots,
        DriftDotsPer100Labels = DefaultDriftDotsPer100Labels,
    };

    public LotLabelCalibration(int pitchDots, int driftDotsPer100Labels, string modifiedBy)
    {
        Id = 1;
        PitchDots = pitchDots;
        DriftDotsPer100Labels = driftDotsPer100Labels;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns a corrected copy from an operator's observation of how the printed text
    /// walks across the label face. Text drifting down means the media is advancing too
    /// slowly, so the gap between labels has to grow; drifting up means the opposite.
    /// At a limit the copy holds the current value unchanged, which callers detect by
    /// comparing <see cref="EffectivePitchHundredths"/>.
    /// </summary>
    public LotLabelCalibration Nudge(LabelDriftDirection direction, LabelDriftSpeed speed, string modifiedBy)
    {
        var magnitude = speed switch
        {
            LabelDriftSpeed.Fast => NudgeFastHundredths,
            LabelDriftSpeed.Slow => NudgeSlowHundredths,
            _ => throw new ArgumentOutOfRangeException(nameof(speed), speed, "Unknown drift speed."),
        };
        var delta = direction switch
        {
            LabelDriftDirection.Down => magnitude,
            LabelDriftDirection.Up => -magnitude,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown drift direction."),
        };

        var current = EffectivePitchHundredths;
        var corrected = Math.Clamp(
            current + delta, MinEffectivePitchHundredths, MaxEffectivePitchHundredths);

        // Clamping must never walk the calibration the opposite way from what was reported.
        // It would, for a value the advanced form put above the wizard's ceiling. Hold the
        // stored pair exactly as it is instead: re-deriving it from an out-of-range
        // effective pitch would produce a pitch outside its own range.
        if (Math.Sign(corrected - current) != Math.Sign(delta))
        {
            return new LotLabelCalibration(PitchDots, DriftDotsPer100Labels, modifiedBy);
        }

        return new LotLabelCalibration(corrected / 100, corrected % 100, modifiedBy);
    }

    internal void Update(int pitchDots, int driftDotsPer100Labels, string modifiedBy)
    {
        PitchDots = pitchDots;
        DriftDotsPer100Labels = driftDotsPer100Labels;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
