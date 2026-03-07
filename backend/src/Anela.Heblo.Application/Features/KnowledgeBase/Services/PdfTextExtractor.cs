using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PdfTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from PDF ({Bytes} bytes)", content.Length);

        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().Select(p => p.Text);
        var text = string.Join("\n\n", pages);

        _logger.LogDebug("Extracted {CharCount} characters from {PageCount} pages",
            text.Length, document.NumberOfPages);

        return Task.FromResult(text);
    }
}
