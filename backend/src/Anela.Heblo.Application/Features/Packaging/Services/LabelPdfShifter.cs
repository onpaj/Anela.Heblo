using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Moves a label's content towards the left edge of the printed page by sliding each page's
/// boxes (MediaBox, CropBox, …) the other way. Page content is not re-rendered, so the barcode
/// stays vector-sharp; the content cut off at the left becomes blank space at the right.
/// </summary>
public static class LabelPdfShifter
{
    private static readonly string[] PageBoxKeys = ["/MediaBox", "/CropBox", "/BleedBox", "/TrimBox", "/ArtBox"];

    public static byte[] ShiftLeft(byte[] pdfBytes, double distancePoints)
    {
        using var input = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages.Cast<PdfPage>())
            ShiftPageBoxes(page, distancePoints);

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void ShiftPageBoxes(PdfPage page, double distancePoints)
    {
        var (dx, dy) = TowardsPrintedRightEdge(page.Rotate, distancePoints);

        foreach (var key in PageBoxKeys)
        {
            if (!page.Elements.ContainsKey(key))
                continue;

            var box = page.Elements.GetRectangle(key);
            page.Elements.SetRectangle(key, new PdfRectangle(new XPoint(box.X1 + dx, box.Y1 + dy), new XPoint(box.X2 + dx, box.Y2 + dy)));
        }
    }

    /// <summary>
    /// /Rotate turns the page clockwise for display, so the user-space axis that points to the
    /// printed page's right edge is +x, +y, -x, -y for 0°, 90°, 180°, 270°.
    /// </summary>
    private static (double Dx, double Dy) TowardsPrintedRightEdge(int rotate, double distance) =>
        ((rotate % 360 + 360) % 360) switch
        {
            90 => (0, distance),
            180 => (-distance, 0),
            270 => (0, -distance),
            _ => (distance, 0),
        };
}
