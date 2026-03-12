namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
