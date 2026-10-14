using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;

public class GetQuestionTagStatsHandler : IRequestHandler<GetQuestionTagStatsRequest, GetQuestionTagStatsResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly IProductEnrichmentCache _productCache;

    public GetQuestionTagStatsHandler(IRagInteractionLogRepository repository, IProductEnrichmentCache productCache)
    {
        _repository = repository;
        _productCache = productCache;
    }

    public async Task<GetQuestionTagStatsResponse> Handle(GetQuestionTagStatsRequest request, CancellationToken cancellationToken)
    {
        var counts = await _repository.GetTagCountsAsync(
            RagFeature.KnowledgeBase,
            PragueDateRange.StartOfDay(request.DateFrom),
            PragueDateRange.EndExclusive(request.DateTo),
            cancellationToken);

        var recommended = OfKind(counts, RagTagKind.RecommendedProduct);
        IReadOnlyDictionary<string, ProductEnrichmentEntry> productNames = recommended.Count == 0
            ? new Dictionary<string, ProductEnrichmentEntry>()
            : await _productCache.GetProductLookupAsync(cancellationToken);

        return new GetQuestionTagStatsResponse
        {
            ProblemTags = ToDtos(OfKind(counts, RagTagKind.Problem), tag => tag),
            ProductTags = ToDtos(OfKind(counts, RagTagKind.Product), tag => tag),
            RecommendedProducts = ToDtos(recommended, code => productNames.TryGetValue(code, out var p) ? p.ProductName : code),
        };
    }

    private static List<RagTagCount> OfKind(IReadOnlyList<RagTagCount> counts, RagTagKind kind) =>
        counts
            .Where(c => c.Kind == kind)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Tag, StringComparer.Ordinal)
            .ToList();

    private static List<QuestionTagCountDto> ToDtos(IEnumerable<RagTagCount> counts, Func<string, string> label) =>
        counts.Select(c => new QuestionTagCountDto { Tag = c.Tag, Label = label(c.Tag), Count = c.Count }).ToList();
}
