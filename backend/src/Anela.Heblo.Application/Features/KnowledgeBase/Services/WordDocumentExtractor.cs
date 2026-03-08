using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class WordDocumentExtractor : IDocumentTextExtractor
{
    private readonly ILogger<WordDocumentExtractor> _logger;

    public WordDocumentExtractor(ILogger<WordDocumentExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.Equals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from Word document ({Bytes} bytes)", content.Length);

        using var stream = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        var text = string.Join("\n\n", paragraphs);

        _logger.LogDebug("Extracted {CharCount} characters from Word document", text.Length);
        return Task.FromResult(text);
    }
}
