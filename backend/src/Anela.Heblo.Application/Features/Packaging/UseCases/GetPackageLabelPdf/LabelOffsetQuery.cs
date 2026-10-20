using System.Globalization;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;

/// <summary>How far one carrier's label is moved, in whole millimetres towards the PDF page's right (X) and top (Y) edge.</summary>
public readonly record struct LabelOffset(int XMm, int YMm);

/// <summary>
/// Parses the per-computer label offsets the packing-desk browser sends with every label request:
/// comma-separated <c>Carrier:xMm:yMm</c> entries, e.g. <c>Zasilkovna:0:-5,PPL:2:0</c>.
/// The offsets live only in that computer's browser, so each printer/PC pair is tuned on its own.
/// </summary>
public static class LabelOffsetQuery
{
    /// <summary>Largest move allowed in either direction on either axis.</summary>
    public const int MaxOffsetMm = 30;

    /// <summary>Carriers whose labels are printed at the packing desk and can be offset.</summary>
    private static readonly IReadOnlyDictionary<string, Carriers> LabelCarriers = new Dictionary<string, Carriers>
    {
        [nameof(Carriers.Zasilkovna)] = Carriers.Zasilkovna,
        [nameof(Carriers.PPL)] = Carriers.PPL,
        [nameof(Carriers.GLS)] = Carriers.GLS,
    };

    /// <summary>
    /// Returns false for any malformed, unknown-carrier, duplicate or out-of-range entry. A null or
    /// empty value is valid and yields no offsets.
    /// </summary>
    public static bool TryParse(string? value, out IReadOnlyDictionary<Carriers, LabelOffset> offsets)
    {
        offsets = new Dictionary<Carriers, LabelOffset>();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var parsed = new Dictionary<Carriers, LabelOffset>();
        foreach (var entry in value.Split(','))
        {
            var parts = entry.Split(':');
            if (parts.Length != 3
                || !LabelCarriers.TryGetValue(parts[0].Trim(), out var carrier)
                || !TryParseMm(parts[1], out var xMm)
                || !TryParseMm(parts[2], out var yMm)
                || !parsed.TryAdd(carrier, new LabelOffset(xMm, yMm)))
            {
                return false;
            }
        }

        offsets = parsed;
        return true;
    }

    private static bool TryParseMm(string value, out int mm) =>
        int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out mm)
        && mm is >= -MaxOffsetMm and <= MaxOffsetMm;
}
