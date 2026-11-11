using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Moves a label's content on the printed page by changing each page's boxes (MediaBox, CropBox, …).
/// Page content is not re-rendered, so the barcode stays vector-sharp. Each printed axis decides on its own:
/// <list type="bullet">
/// <item>Content moving right or down: the window grows on the opposite side and the edge the content
/// moves towards stays, so the page gets longer by the offset and nothing is cut off.</item>
/// <item>Content moving left or up: the window slides at the same size. The print places the page from
/// its left / top edge, so growing there would not move the content; content pushed past that edge is
/// cut off and the opposite edge gains the same amount of blank space. That anchoring is inferred from
/// one printed label, not verified against the print dialog.</item>
/// </list>
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

        (double X, double Y) ToUserSpace(double right, double up) =>
            (right * rightX + up * upX, right * rightY + up * upY);

        // The content's move in user space, split by how the window absorbs it.
        var slide = ToUserSpace(Math.Min(rightPoints, 0), Math.Max(upPoints, 0));
        var grow = ToUserSpace(Math.Max(rightPoints, 0), Math.Min(upPoints, 0));

        foreach (var key in PageBoxKeys)
        {
            if (!page.Elements.ContainsKey(key))
                continue;

            var box = page.Elements.GetRectangle(key);

            // A sliding window moves opposite to the content; a growing one moves only the edge the
            // content leaves behind. A PDF rectangle may name any two opposite corners, hence Min/Max.
            var lowX = Math.Min(box.X1, box.X2) - slide.X - Math.Max(grow.X, 0);
            var lowY = Math.Min(box.Y1, box.Y2) - slide.Y - Math.Max(grow.Y, 0);
            var highX = Math.Max(box.X1, box.X2) - slide.X - Math.Min(grow.X, 0);
            var highY = Math.Max(box.Y1, box.Y2) - slide.Y - Math.Min(grow.Y, 0);

            page.Elements.SetRectangle(key, new PdfRectangle(new XPoint(lowX, lowY), new XPoint(highX, highY)));
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
