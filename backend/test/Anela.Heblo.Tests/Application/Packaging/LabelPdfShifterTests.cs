using Anela.Heblo.Application.Features.Packaging.Services;
using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Tests.Application.Packaging;

public class LabelPdfShifterTests
{
    private const double Distance = 28.0;
    private const double Width = 288.0;
    private const double Height = 432.0;

    private static readonly string[] InnerPageBoxKeys = ["/CropBox", "/BleedBox", "/TrimBox", "/ArtBox"];

    private static byte[] CreatePdf(int pageRotation = 0, bool withInnerBoxes = false)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Elements.SetRectangle("/MediaBox", new PdfRectangle(new XPoint(0, 0), new XPoint(Width, Height)));
        if (withInnerBoxes)
        {
            foreach (var key in InnerPageBoxKeys)
                page.Elements.SetRectangle(key, new PdfRectangle(new XPoint(10, 20), new XPoint(Width - 10, Height - 20)));
        }
        page.Rotate = pageRotation;

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    // Hand-written so /MediaBox, /CropBox and /Rotate live only on the /Pages node and the page inherits them.
    private static byte[] CreatePdfWithInheritedPageAttributes(int pageRotation)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            FormattableString.Invariant($"<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 {Width} {Height}] /CropBox [10 20 {Width - 10} {Height - 20}] /Rotate {pageRotation} >>"),
            "<< /Type /Page /Parent 2 0 R >>",
        };

        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static PdfRectangle Box(byte[] pdfBytes, string key)
    {
        using var document = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        return document.Pages[0].Elements.GetRectangle(key);
    }

    private static void AssertEdgesMoved(PdfRectangle box, PdfRectangle original, double x1, double y1, double x2, double y2)
    {
        box.X1.Should().BeApproximately(original.X1 + x1, 0.001);
        box.Y1.Should().BeApproximately(original.Y1 + y1, 0.001);
        box.X2.Should().BeApproximately(original.X2 + x2, 0.001);
        box.Y2.Should().BeApproximately(original.Y2 + y2, 0.001);
    }

    // Content moving right or down grows the window on the opposite printed side; the edge it moves
    // towards stays. Content moving left or up slides the whole window the opposite way.
    // The printed right / top edges are +x/+y, +y/-x, -x/-y, -y/+x for 0°, 90°, 180°, 270°.
    [Theory]
    // Right: the printed left edge moves out.
    [InlineData(0, Distance, 0, -Distance, 0, 0, 0)]
    [InlineData(90, Distance, 0, 0, -Distance, 0, 0)]
    [InlineData(180, Distance, 0, 0, 0, Distance, 0)]
    [InlineData(270, Distance, 0, 0, 0, 0, Distance)]
    [InlineData(-90, Distance, 0, 0, 0, 0, Distance)]
    // Down: the printed top edge moves out.
    [InlineData(0, 0, -Distance, 0, 0, 0, Distance)]
    [InlineData(90, 0, -Distance, -Distance, 0, 0, 0)]
    [InlineData(180, 0, -Distance, 0, -Distance, 0, 0)]
    [InlineData(270, 0, -Distance, 0, 0, Distance, 0)]
    [InlineData(-90, 0, -Distance, 0, 0, Distance, 0)]
    // Left: the whole window slides towards the printed right.
    [InlineData(0, -Distance, 0, Distance, 0, Distance, 0)]
    [InlineData(90, -Distance, 0, 0, Distance, 0, Distance)]
    [InlineData(180, -Distance, 0, -Distance, 0, -Distance, 0)]
    [InlineData(270, -Distance, 0, 0, -Distance, 0, -Distance)]
    [InlineData(-90, -Distance, 0, 0, -Distance, 0, -Distance)]
    // Up: the whole window slides towards the printed bottom.
    [InlineData(0, 0, Distance, 0, -Distance, 0, -Distance)]
    [InlineData(90, 0, Distance, Distance, 0, Distance, 0)]
    [InlineData(180, 0, Distance, 0, Distance, 0, Distance)]
    [InlineData(270, 0, Distance, -Distance, 0, -Distance, 0)]
    [InlineData(-90, 0, Distance, -Distance, 0, -Distance, 0)]
    // Both axes at once: each decides on its own.
    [InlineData(0, Distance, -Distance, -Distance, 0, 0, Distance)]
    [InlineData(0, Distance, Distance, -Distance, -Distance, 0, -Distance)]
    [InlineData(0, -Distance, -Distance, Distance, 0, Distance, Distance)]
    [InlineData(0, -Distance, Distance, Distance, -Distance, Distance, -Distance)]
    [InlineData(90, Distance, -Distance, -Distance, -Distance, 0, 0)]
    [InlineData(180, -Distance, -Distance, -Distance, -Distance, -Distance, 0)]
    [InlineData(270, Distance, Distance, -Distance, 0, -Distance, Distance)]
    public void Shift_MovesEachMediaBoxEdgeForThePrintedContentMove(
        int pageRotation, double right, double up, double x1, double y1, double x2, double y2)
    {
        var original = CreatePdf(pageRotation);

        var shifted = LabelPdfShifter.Shift(original, right, up);

        AssertEdgesMoved(Box(shifted, "/MediaBox"), Box(original, "/MediaBox"), x1, y1, x2, y2);
    }

    [Theory]
    [InlineData(0, Distance, 0, -Distance, 0, 0, 0)]
    [InlineData(0, 0, -Distance, 0, 0, 0, Distance)]
    [InlineData(0, -Distance, 0, Distance, 0, Distance, 0)]
    [InlineData(0, 0, Distance, 0, -Distance, 0, -Distance)]
    [InlineData(90, Distance, 0, 0, -Distance, 0, 0)]
    [InlineData(90, 0, -Distance, -Distance, 0, 0, 0)]
    [InlineData(90, -Distance, 0, 0, Distance, 0, Distance)]
    [InlineData(90, 0, Distance, Distance, 0, Distance, 0)]
    public void Shift_MovesEveryPageBoxTheSameWay(
        int pageRotation, double right, double up, double x1, double y1, double x2, double y2)
    {
        var original = CreatePdf(pageRotation, withInnerBoxes: true);

        var shifted = LabelPdfShifter.Shift(original, right, up);

        foreach (var key in InnerPageBoxKeys.Append("/MediaBox"))
            AssertEdgesMoved(Box(shifted, key), Box(original, key), x1, y1, x2, y2);
    }

    [Theory]
    [InlineData(0, Distance, 0, -Distance, 0, 0, 0)]
    [InlineData(0, 0, -Distance, 0, 0, 0, Distance)]
    [InlineData(0, -Distance, 0, Distance, 0, Distance, 0)]
    [InlineData(90, Distance, 0, 0, -Distance, 0, 0)]
    [InlineData(90, 0, -Distance, -Distance, 0, 0, 0)]
    [InlineData(90, -Distance, 0, 0, Distance, 0, Distance)]
    public void Shift_MovesBoxesInheritedFromThePagesNode(
        int pageRotation, double right, double up, double x1, double y1, double x2, double y2)
    {
        var original = CreatePdfWithInheritedPageAttributes(pageRotation);

        var shifted = LabelPdfShifter.Shift(original, right, up);

        AssertEdgesMoved(Box(shifted, "/MediaBox"), Box(original, "/MediaBox"), x1, y1, x2, y2);
        AssertEdgesMoved(Box(shifted, "/CropBox"), Box(original, "/CropBox"), x1, y1, x2, y2);
    }

    [Fact]
    public void Shift_ZeroOffset_LeavesEveryPageBoxUnchanged()
    {
        var original = CreatePdf(withInnerBoxes: true);

        var shifted = LabelPdfShifter.Shift(original, 0, 0);

        foreach (var key in InnerPageBoxKeys.Append("/MediaBox"))
            AssertEdgesMoved(Box(shifted, key), Box(original, key), 0, 0, 0, 0);
    }

    // A PDF rectangle may name any two opposite corners, so "low" must not be read off the array order.
    [Theory]
    [InlineData(Distance, 0, -Distance, 0, 0, 0)]
    [InlineData(0, -Distance, 0, 0, 0, Distance)]
    [InlineData(-Distance, 0, Distance, 0, Distance, 0)]
    [InlineData(0, Distance, 0, -Distance, 0, -Distance)]
    public void Shift_BoxWrittenWithSwappedCorners_MovesTheSameEdges(
        double right, double up, double lowX, double lowY, double highX, double highY)
    {
        using var document = new PdfDocument();
        document.AddPage().Elements.SetRectangle("/MediaBox", new PdfRectangle(new XPoint(Width, Height), new XPoint(0, 0)));
        using var ms = new MemoryStream();
        document.Save(ms);

        var mediaBox = Box(LabelPdfShifter.Shift(ms.ToArray(), right, up), "/MediaBox");

        Math.Min(mediaBox.X1, mediaBox.X2).Should().BeApproximately(lowX, 0.001);
        Math.Max(mediaBox.X1, mediaBox.X2).Should().BeApproximately(Width + highX, 0.001);
        Math.Min(mediaBox.Y1, mediaBox.Y2).Should().BeApproximately(lowY, 0.001);
        Math.Max(mediaBox.Y1, mediaBox.Y2).Should().BeApproximately(Height + highY, 0.001);
    }

    // The PDF spec only allows multiples of 90, so anything else is treated as an unrotated page.
    [Fact]
    public void Shift_RotationNotAMultipleOf90_TreatsThePageAsUnrotated()
    {
        var original = CreatePdfWithInheritedPageAttributes(pageRotation: 45);

        var shifted = LabelPdfShifter.Shift(original, Distance, 0);

        AssertEdgesMoved(Box(shifted, "/MediaBox"), Box(original, "/MediaBox"), -Distance, 0, 0, 0);
    }

    [Fact]
    public void Shift_KeepsPageRotation()
    {
        var shifted = LabelPdfShifter.Shift(CreatePdf(pageRotation: 90), -Distance, 0);

        using var document = PdfReader.Open(new MemoryStream(shifted), PdfDocumentOpenMode.Import);
        document.Pages[0].Rotate.Should().Be(90);
    }
}
