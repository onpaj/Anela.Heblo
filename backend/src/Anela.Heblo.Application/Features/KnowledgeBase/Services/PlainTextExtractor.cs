using System.Text;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PlainTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PlainTextExtractor> _logger;

    public PlainTextExtractor(ILogger<PlainTextExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/markdown", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from plain text file ({Bytes} bytes)", content.Length);
        return Task.FromResult(Encoding.UTF8.GetString(content));
    }
}
