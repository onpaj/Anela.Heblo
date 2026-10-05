using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Anela.Heblo.Application.Features.Packaging.Services;

/// <summary>
/// Turns a label PDF upside down by adding 180° to each page's /Rotate entry. Page content is
/// not re-rendered, so the barcode stays vector-sharp; the viewer/printer applies the rotation.
/// </summary>
public static class LabelPdfRotator
{
    private const int HalfTurnDegrees = 180;
    private const int FullTurnDegrees = 360;

    public static byte[] RotateHalfTurn(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages.Cast<PdfPage>())
            page.Rotate = (page.Rotate + HalfTurnDegrees) % FullTurnDegrees;

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
