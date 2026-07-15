namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

/// <summary>
/// Single-row record of the media type last printed on the shared label printer. Persisted so the
/// application can, on the next print, detect a switch to a different media type and require the
/// operator to confirm they have changed and calibrated the roll. Durable across restarts.
/// </summary>
public class PrinterMediaState
{
    public int Id { get; private set; }
    public LabelMediaType LastMediaType { get; private set; }
    public DateTimeOffset? LastPrintedAt { get; private set; }
    public string? LastPrintedBy { get; private set; }

    private PrinterMediaState() { }

    public static PrinterMediaState CreateDefault() => new()
    {
        Id = 1,
        LastMediaType = LabelMediaType.Unknown,
    };

    /// <summary>
    /// True when printing <paramref name="requested"/> would switch away from a known, different
    /// media type. The first print ever (Unknown) and reprints of the same type never require it.
    /// </summary>
    public bool RequiresConfirmation(LabelMediaType requested) =>
        LastMediaType != LabelMediaType.Unknown && LastMediaType != requested;

    public void RecordPrint(LabelMediaType mediaType, string printedBy)
    {
        LastMediaType = mediaType;
        LastPrintedBy = printedBy;
        LastPrintedAt = DateTimeOffset.UtcNow;
    }
}
