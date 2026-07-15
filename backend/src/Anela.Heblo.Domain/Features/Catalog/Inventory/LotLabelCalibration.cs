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

    public int Id { get; private set; }
    public int PitchDots { get; private set; }
    public int DriftDotsPer100Labels { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

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

    internal void Update(int pitchDots, int driftDotsPer100Labels, string modifiedBy)
    {
        PitchDots = pitchDots;
        DriftDotsPer100Labels = driftDotsPer100Labels;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
