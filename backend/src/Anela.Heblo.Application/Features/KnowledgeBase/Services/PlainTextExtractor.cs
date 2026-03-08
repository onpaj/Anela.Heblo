using System.Text;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PlainTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string contentType)
    {
        var lower = contentType.ToLowerInvariant();
        return lower.StartsWith("text/") || lower == "application/markdown";
    }

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        var text = Encoding.UTF8.GetString(content);
        return Task.FromResult(text);
    }
}
