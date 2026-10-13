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

    private static byte[] CreatePdf(int pageRotation = 0, bool withCropBox = false)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Elements.SetRectangle("/MediaBox", new PdfRectangle(new XPoint(0, 0), new XPoint(Width, Height)));
        if (withCropBox)
            page.Elements.SetRectangle("/CropBox", new PdfRectangle(new XPoint(10, 20), new XPoint(Width - 10, Height - 20)));
        page.Rotate = pageRotation;

        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    // Hand-written so /MediaBox and /Rotate live only on the /Pages node and the page inherits them.
    private static byte[] CreatePdfWithInheritedPageAttributes(int pageRotation)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            FormattableString.Invariant($"<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 {Width} {Height}] /Rotate {pageRotation} >>"),
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

    // Moving the content by (right, up) on the printed page slides the visible window the
    // opposite way along the user-space axes that point to the printed right / top edge after
    // /Rotate: right = +x, +y, -x, -y and up = +y, -x, -y, +x for 0°, 90°, 180°, 270°.
    [Theory]
    [InlineData(0, -Distance, 0, Distance, 0)]
    [InlineData(90, -Distance, 0, 0, Distance)]
    [InlineData(180, -Distance, 0, -Distance, 0)]
    [InlineData(270, -Distance, 0, 0, -Distance)]
    [InlineData(-90, -Distance, 0, 0, -Distance)]
    [InlineData(0, Distance, 0, -Distance, 0)]
    [InlineData(0, 0, Distance, 0, -Distance)]
    [InlineData(0, 0, -Distance, 0, Distance)]
    [InlineData(90, 0, Distance, Distance, 0)]
    [InlineData(180, 0, Distance, 0, Distance)]
    [InlineData(270, 0, Distance, -Distance, 0)]
    [InlineData(0, -Distance, Distance, Distance, -Distance)]
    public void Shift_MovesMediaBoxOppositeToThePrintedContentMove(
        int pageRotation, double right, double up, double dx, double dy)
    {
        var shifted = LabelPdfShifter.Shift(CreatePdf(pageRotation), right, up);

        var mediaBox = Box(shifted, "/MediaBox");
        mediaBox.X1.Should().BeApproximately(dx, 0.001);
        mediaBox.Y1.Should().BeApproximately(dy, 0.001);
        mediaBox.X2.Should().BeApproximately(Width + dx, 0.001);
        mediaBox.Y2.Should().BeApproximately(Height + dy, 0.001);
    }

    [Fact]
    public void Shift_ShiftsCropBoxTogetherWithMediaBox()
    {
        var shifted = LabelPdfShifter.Shift(CreatePdf(withCropBox: true), -Distance, 0);

        var cropBox = Box(shifted, "/CropBox");
        cropBox.X1.Should().BeApproximately(10 + Distance, 0.001);
        cropBox.Y1.Should().BeApproximately(20, 0.001);
        cropBox.X2.Should().BeApproximately(Width - 10 + Distance, 0.001);
        cropBox.Y2.Should().BeApproximately(Height - 20, 0.001);
    }

    [Theory]
    [InlineData(0, Distance, 0)]
    [InlineData(90, 0, Distance)]
    public void Shift_ShiftsMediaBoxInheritedFromThePagesNode(int pageRotation, double dx, double dy)
    {
        var shifted = LabelPdfShifter.Shift(CreatePdfWithInheritedPageAttributes(pageRotation), -Distance, 0);

        var mediaBox = Box(shifted, "/MediaBox");
        mediaBox.X1.Should().BeApproximately(dx, 0.001);
        mediaBox.Y1.Should().BeApproximately(dy, 0.001);
        mediaBox.X2.Should().BeApproximately(Width + dx, 0.001);
        mediaBox.Y2.Should().BeApproximately(Height + dy, 0.001);
    }

    [Fact]
    public void Shift_KeepsPageRotation()
    {
        var shifted = LabelPdfShifter.Shift(CreatePdf(pageRotation: 90), -Distance, 0);

        using var document = PdfReader.Open(new MemoryStream(shifted), PdfDocumentOpenMode.Import);
        document.Pages[0].Rotate.Should().Be(90);
    }
}
