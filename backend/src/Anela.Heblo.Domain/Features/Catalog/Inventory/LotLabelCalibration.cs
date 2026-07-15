namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// Single-row calibration for the round lot-label media: the vertical pitch (in dots) the
/// printer advances per label in continuous mode, plus a drift correction that feeds one
/// extra dot every N labels. Persisted so an administrator can tune it once for all sessions
/// without a code change.
/// </summary>
public class LotLabelCalibration
{
    public const int DefaultPitchDots = 148;
    public const int MinPitchDots = 80;
    public const int MaxPitchDots = 400;

    // Drift correction: feed +1 dot every N labels. 0 disables the correction.
    public const int DefaultDriftEveryNLabels = 3;
    public const int MinDriftEveryNLabels = 0;
    public const int MaxDriftEveryNLabels = 100;

    public int Id { get; private set; }
    public int PitchDots { get; private set; }
    public int DriftEveryNLabels { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private LotLabelCalibration() { }

    public static LotLabelCalibration CreateDefault() => new()
    {
        Id = 1,
        PitchDots = DefaultPitchDots,
        DriftEveryNLabels = DefaultDriftEveryNLabels,
    };

    public LotLabelCalibration(int pitchDots, int driftEveryNLabels, string modifiedBy)
    {
        Id = 1;
        PitchDots = pitchDots;
        DriftEveryNLabels = driftEveryNLabels;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    internal void Update(int pitchDots, int driftEveryNLabels, string modifiedBy)
    {
        PitchDots = pitchDots;
        DriftEveryNLabels = driftEveryNLabels;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
