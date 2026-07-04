namespace Anela.Heblo.Application.Shared.Rag;

public interface IDocumentTextExtractor
{
    bool CanHandle(string contentType);
    Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
}
