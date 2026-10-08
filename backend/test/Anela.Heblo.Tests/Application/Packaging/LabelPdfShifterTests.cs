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

    private static PdfRectangle Box(byte[] pdfBytes, string key)
    {
        using var document = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        return document.Pages[0].Elements.GetRectangle(key);
    }

    // Shifting the visible window by +d moves the content by -d on the printed page. The window
    // moves along whichever user-space axis points to the printed page's right edge after /Rotate.
    [Theory]
    [InlineData(0, Distance, 0)]
    [InlineData(90, 0, Distance)]
    [InlineData(180, -Distance, 0)]
    [InlineData(270, 0, -Distance)]
    public void ShiftLeft_MovesMediaBoxTowardsThePrintedRightEdge(int pageRotation, double dx, double dy)
    {
        var shifted = LabelPdfShifter.ShiftLeft(CreatePdf(pageRotation), Distance);

        var mediaBox = Box(shifted, "/MediaBox");
        mediaBox.X1.Should().BeApproximately(dx, 0.001);
        mediaBox.Y1.Should().BeApproximately(dy, 0.001);
        mediaBox.X2.Should().BeApproximately(Width + dx, 0.001);
        mediaBox.Y2.Should().BeApproximately(Height + dy, 0.001);
    }

    [Fact]
    public void ShiftLeft_ShiftsCropBoxTogetherWithMediaBox()
    {
        var shifted = LabelPdfShifter.ShiftLeft(CreatePdf(withCropBox: true), Distance);

        var cropBox = Box(shifted, "/CropBox");
        cropBox.X1.Should().BeApproximately(10 + Distance, 0.001);
        cropBox.Y1.Should().BeApproximately(20, 0.001);
        cropBox.X2.Should().BeApproximately(Width - 10 + Distance, 0.001);
        cropBox.Y2.Should().BeApproximately(Height - 20, 0.001);
    }

    [Fact]
    public void ShiftLeft_KeepsPageRotation()
    {
        var shifted = LabelPdfShifter.ShiftLeft(CreatePdf(pageRotation: 90), Distance);

        using var document = PdfReader.Open(new MemoryStream(shifted), PdfDocumentOpenMode.Import);
        document.Pages[0].Rotate.Should().Be(90);
    }
}
