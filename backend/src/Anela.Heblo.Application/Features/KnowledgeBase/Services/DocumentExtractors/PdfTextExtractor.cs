using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;

public class PdfTextExtractor : IDocumentTextExtractor
{
    private static readonly System.Text.RegularExpressions.Regex MultipleWhitespaceRegex =
        new(@"[ \t]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

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
        var pageTexts = document.GetPages()
            .Select(p => CleanPageText(ContentOrderTextExtractor.GetText(p)));
        var text = string.Join("\n\n", pageTexts);

        _logger.LogDebug("Extracted {CharCount} characters from {PageCount} pages",
            text.Length, document.NumberOfPages);

        return Task.FromResult(text);
    }

    internal static string CleanPageText(string raw)
    {
        // Replace unmappable glyphs (incomplete ToUnicode CMap in InDesign-subset fonts)
        // with space so adjacent words don't fuse, then normalize whitespace runs.
        var withoutReplacementChars = raw.Replace('�', ' ');
        return MultipleWhitespaceRegex.Replace(withoutReplacementChars, " ").Trim();
    }
}
