namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public interface IProductEnrichmentCache
{
    Task<IReadOnlyDictionary<string, ProductEnrichmentEntry>> GetProductLookupAsync(
        CancellationToken ct = default);
}
