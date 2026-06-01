using System.Text;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.Printing;

public static class MaterialContainerLabelZplBuilder
{
    private const int LabelWidthDots = 406;   // ~50 mm @ 203 dpi
    private const int LabelHeightDots = 203;  // ~25 mm @ 203 dpi

    public static string Build(IReadOnlyCollection<string> codes)
    {
        if (codes is null || codes.Count == 0)
            throw new ArgumentException("At least one code is required.", nameof(codes));

        var sb = new StringBuilder();
        foreach (var code in codes)
        {
            sb.Append("^XA");
            sb.Append($"^PW{LabelWidthDots}");
            sb.Append($"^LL{LabelHeightDots}");
            sb.Append("^FO30,25^BY2");
            sb.Append("^BCN,90,N,N,N");          // Code128, height 90, no embedded text
            sb.Append($"^FD{code}^FS");
            sb.Append($"^FO30,135^A0N,30,30^FD{code}^FS");  // human-readable code
            sb.Append("^XZ");
        }
        return sb.ToString();
    }
}
