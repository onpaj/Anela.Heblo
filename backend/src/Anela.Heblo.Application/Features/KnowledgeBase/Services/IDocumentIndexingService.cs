using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IDocumentIndexingService
{
    /// <summary>
    /// Extracts text from <paramref name="content"/>, chunks it, generates embeddings,
    /// persists the chunks, and marks <paramref name="document"/> as Indexed.
    /// Does NOT call SaveChanges — caller is responsible.
    /// Throws <see cref="NotSupportedException"/> if no extractor handles <paramref name="contentType"/>.
    /// </summary>
    Task IndexChunksAsync(
        byte[] content,
        string contentType,
        KnowledgeBaseDocument document,
        CancellationToken ct = default);
}
