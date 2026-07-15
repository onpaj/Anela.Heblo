using System.Text;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.Printing;

public static class MaterialContainerLabelZplBuilder
{
    private const int LabelWidthDots = 590;     // 50 mm @ 300 dpi
    private const int LabelHeightDots = 236;    // 20 mm @ 300 dpi
    private const int BarcodeHeightDots = 118;  // 10 mm @ 300 dpi
    private const int BarcodeModuleWidth = 3;   // ^BY3

    public static string Build(IReadOnlyCollection<string> codes)
    {
        if (codes is null || codes.Count == 0)
            throw new ArgumentException("At least one code is required.", nameof(codes));

        var sb = new StringBuilder();
        foreach (var code in codes)
        {
            sb.Append("^XA");
            sb.Append("^MNY");  // non-continuous gap media: sense the web between labels.
                                // Set explicitly so container prints keep working after a
                                // lot-label print switched the printer to continuous mode.
            sb.Append($"^PW{LabelWidthDots}");
            sb.Append($"^LL{LabelHeightDots}");
            // Code128 @ 300 dpi, module width BY3, 10 mm tall. ^FB centers text but NOT a
            // barcode field, so the barcode is centered with an explicit origin computed from
            // its deterministic width: Subset B (mode N) encodes (chars + 2) symbols at 11
            // modules each plus a 13-module stop.
            var barcodeWidthDots = ((code.Length + 2) * 11 + 13) * BarcodeModuleWidth;
            var barcodeLeftDots = Math.Max(0, (LabelWidthDots - barcodeWidthDots) / 2);
            sb.Append($"^FO{barcodeLeftDots},20^BY{BarcodeModuleWidth}");
            sb.Append($"^BCN,{BarcodeHeightDots},N,N,N");  // Code128, no embedded text
            sb.Append($"^FD{code}^FS");
            // Human-readable code, enlarged and centered under the barcode (~40 mm wide).
            sb.Append($"^FO0,150^A0N,60,52^FB{LabelWidthDots},1,0,C,0^FD{code}^FS");
            sb.Append("^XZ");
        }
        return sb.ToString();
    }
}
