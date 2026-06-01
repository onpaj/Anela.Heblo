using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IIndexingStrategy
{
    bool Supports(DocumentType documentType);
    Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct);
}
