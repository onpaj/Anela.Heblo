namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// Single-row calibration for the round lot-label media: the vertical pitch (in dots) the
/// printer advances per label in continuous mode. Persisted so an administrator can tune it
/// once for all sessions without a code change.
/// </summary>
public class LotLabelCalibration
{
    public const int DefaultPitchDots = 148;
    public const int MinPitchDots = 80;
    public const int MaxPitchDots = 400;

    public int Id { get; private set; }
    public int PitchDots { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private LotLabelCalibration() { }

    public static LotLabelCalibration CreateDefault() => new() { Id = 1, PitchDots = DefaultPitchDots };

    public LotLabelCalibration(int pitchDots, string modifiedBy)
    {
        Id = 1;
        PitchDots = pitchDots;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    internal void Update(int pitchDots, string modifiedBy)
    {
        PitchDots = pitchDots;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
