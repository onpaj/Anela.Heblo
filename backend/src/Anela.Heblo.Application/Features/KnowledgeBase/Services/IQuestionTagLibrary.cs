using Anela.Heblo.Application.Shared.Rag;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Injects the KB's already-used question tags into the expansion prompt so the model reuses them,
/// letting the tag vocabulary build itself. Callable outside the Ask path (e.g. a backfill job).
/// </summary>
public interface IQuestionTagLibrary
{
    Task<RagQueryExpansionConfig> BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken ct);
}
