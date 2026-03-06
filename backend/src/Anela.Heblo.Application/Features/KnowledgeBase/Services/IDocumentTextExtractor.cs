namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IDocumentTextExtractor
{
    bool CanHandle(string contentType);
    Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
}
