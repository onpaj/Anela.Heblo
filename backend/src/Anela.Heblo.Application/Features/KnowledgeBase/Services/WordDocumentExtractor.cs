using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class WordDocumentExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> SupportedTypes =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/docx"
    ];

    public bool CanHandle(string contentType) =>
        SupportedTypes.Contains(contentType.ToLowerInvariant());

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return Task.FromResult(sb.ToString());
    }
}
