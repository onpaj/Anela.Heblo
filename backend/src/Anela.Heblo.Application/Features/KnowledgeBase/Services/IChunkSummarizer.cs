namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IChunkSummarizer
{
    Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default);
}
