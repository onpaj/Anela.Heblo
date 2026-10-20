using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Moves a label's content on the printed page by sliding each page's boxes (MediaBox, CropBox, …)
/// the opposite way. Page content is not re-rendered, so the barcode stays vector-sharp; content
/// pushed past an edge is cut off and the opposite edge gains the same amount of blank space.
/// </summary>
public static class LabelPdfShifter
{
    private static readonly string[] PageBoxKeys = ["/MediaBox", "/CropBox", "/BleedBox", "/TrimBox", "/ArtBox"];

    /// <param name="rightPoints">How far the content moves towards the printed right edge (negative = left).</param>
    /// <param name="upPoints">How far the content moves towards the printed top edge (negative = down).</param>
    public static byte[] Shift(byte[] pdfBytes, double rightPoints, double upPoints)
    {
        using var input = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages.Cast<PdfPage>())
            ShiftPageBoxes(page, rightPoints, upPoints);

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void ShiftPageBoxes(PdfPage page, double rightPoints, double upPoints)
    {
        var (rightX, rightY) = PrintedRightAxis(page.Rotate);
        var (upX, upY) = (-rightY, rightX);

        // The window moves opposite to the content.
        var dx = -(rightPoints * rightX + upPoints * upX);
        var dy = -(rightPoints * rightY + upPoints * upY);

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
    /// printed page's right edge is +x, +y, -x, -y for 0°, 90°, 180°, 270°. The printed top edge
    /// is that axis turned 90° counter-clockwise.
    /// </summary>
    private static (double X, double Y) PrintedRightAxis(int rotate) =>
        ((rotate % 360 + 360) % 360) switch
        {
            90 => (0, 1),
            180 => (-1, 0),
            270 => (0, -1),
            _ => (1, 0),
        };
}
