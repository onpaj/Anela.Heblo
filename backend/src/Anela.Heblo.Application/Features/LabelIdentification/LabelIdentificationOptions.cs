namespace Anela.Heblo.Application.Features.LabelIdentification;

public class LabelIdentificationOptions
{
    public const string SectionKey = "LabelIdentification";

    /// <summary>Blended score at or above which a match may auto-confirm.</summary>
    public double AutoConfirmScore { get; set; } = 90;

    /// <summary>Required lead over the runner-up for auto-confirmation.</summary>
    public double AutoConfirmMargin { get; set; } = 5;

    /// <summary>Below this blended score the result is reported as unreadable.</summary>
    public double LowConfidenceFloor { get; set; } = 60;

    /// <summary>Longest edge, in px, the photo is downscaled to before the vision call.</summary>
    public int MaxImageEdge { get; set; } = 2048;

    /// <summary>Upload size cap in bytes.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
