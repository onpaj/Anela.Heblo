using System.Text;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.Printing;

public static class LotLabelZplBuilder
{
    private const int LabelWidthDots = 118;   // 10 mm @ 300 dpi

    // Two centered text lines (same font) stacked around the vertical middle of the face.
    private const int LineFontDots = 30;        // shared font height for both lines
    private const int LotNumberTopDots = 24;    // upper line origin
    private const int ExpirationTopDots = 64;   // lower line origin

    private const int CrossLineThicknessDots = 3;  // calibration crosshair line thickness

    // Even at the calibrated pitch the media drifts slightly across a run, so one extra dot
    // is fed every Nth label to spread ~1 dot of correction over that many labels.
    private const int DriftCorrectionEveryNLabels = 3;

    // Vertical pitch (^LL) the printer advances per label in continuous mode. Round die-cut
    // labels are unreliable for the gap sensor, so the media is driven continuous (^MNN) and
    // fed exactly this many dots; it must match the physical pitch or the print drifts. The
    // value is calibrated per printer and passed in from the persisted LotLabelCalibration.
    public static string Build(string lotNumber, string expiration, int count, int pitchDots)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
            throw new ArgumentException("Lot number is required.", nameof(lotNumber));
        if (string.IsNullOrWhiteSpace(expiration))
            throw new ArgumentException("Expiration is required.", nameof(expiration));
        if (count < 1)
            throw new ArgumentException("Count must be at least 1.", nameof(count));
        if (pitchDots < 1)
            throw new ArgumentException("Pitch must be at least 1 dot.", nameof(pitchDots));

        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            // Add 1 dot on every Nth label to compensate residual drift over the run.
            var labelPitch = pitchDots + ((i + 1) % DriftCorrectionEveryNLabels == 0 ? 1 : 0);

            sb.Append("^XA");
            sb.Append("^MNN");  // continuous media: do not sense gaps/marks, feed by ^LL
            sb.Append($"^PW{LabelWidthDots}");
            sb.Append($"^LL{labelPitch}");
            // Text-only round label. ^FB centers each line horizontally across the full
            // label width; there is no barcode because a linear/2D symbology will not fit
            // a 10 mm round face.
            sb.Append($"^FO0,{LotNumberTopDots}^A0N,{LineFontDots},{LineFontDots}^FB{LabelWidthDots},1,0,C,0^FD{lotNumber}^FS");
            sb.Append($"^FO0,{ExpirationTopDots}^A0N,{LineFontDots},{LineFontDots}^FB{LabelWidthDots},1,0,C,0^FD{expiration}^FS");
            sb.Append("^XZ");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds a single label with a centered crosshair spanning the full label face.
    /// Used to align round media in continuous mode: when the media is positioned
    /// correctly the four line ends touch the circle's top/bottom/left/right edges.
    /// </summary>
    public static string BuildCalibrationCross(int pitchDots)
    {
        if (pitchDots < 1)
            throw new ArgumentException("Pitch must be at least 1 dot.", nameof(pitchDots));

        var offset = (LabelWidthDots - CrossLineThicknessDots) / 2;

        var sb = new StringBuilder();
        sb.Append("^XA");
        sb.Append("^MNN");  // continuous media, same setup as a real lot label
        sb.Append($"^PW{LabelWidthDots}");
        sb.Append($"^LL{pitchDots}");
        // Vertical line (full height) and horizontal line (full width) through the center.
        sb.Append($"^FO{offset},0^GB{CrossLineThicknessDots},{LabelWidthDots},{CrossLineThicknessDots}^FS");
        sb.Append($"^FO0,{offset}^GB{LabelWidthDots},{CrossLineThicknessDots},{CrossLineThicknessDots}^FS");
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a blank label that advances the media forward by exactly <paramref name="dots"/>
    /// dots. Used to nudge round media into alignment on continuous stock (thermal printers
    /// only feed forward, so this cannot move the media backwards).
    /// </summary>
    public static string BuildMediaFeed(int dots)
    {
        if (dots < 1)
            throw new ArgumentException("Feed distance must be at least 1 dot.", nameof(dots));

        // The printer skips a label that has no printable content, so it would not advance
        // on a truly blank format. A 2-dot mark in the top-left corner (off the round label
        // face, on the liner) forces the label to render and feed exactly ^LL dots.
        return $"^XA^MNN^PW{LabelWidthDots}^LL{dots}^FO0,0^GB2,2,2^FS^XZ";
    }
}
