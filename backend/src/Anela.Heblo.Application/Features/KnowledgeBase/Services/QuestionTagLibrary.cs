using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public sealed class QuestionTagLibrary : IQuestionTagLibrary
{
    public const string Placeholder = "{EXISTING_TAGS}";
    public const int MaxLibraryTagsPerKind = 200;
    public const string EmptyLibraryText = "(zatím žádné)";

    private readonly IRagInteractionLogRepository _repository;
    private readonly ILogger<QuestionTagLibrary> _logger;

    public QuestionTagLibrary(IRagInteractionLogRepository repository, ILogger<QuestionTagLibrary> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<RagQueryExpansionConfig> BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken ct)
    {
        if (!baseConfig.Prompt.Contains(Placeholder, StringComparison.Ordinal))
            return baseConfig;

        var counts = await LoadCountsAsync(ct);
        var library = $"Problémy: {Format(counts, RagTagKind.Problem)}\nProdukty: {Format(counts, RagTagKind.Product)}";

        return baseConfig with { Prompt = baseConfig.Prompt.Replace(Placeholder, library, StringComparison.Ordinal) };
    }

    private async Task<IReadOnlyList<RagTagCount>> LoadCountsAsync(CancellationToken ct)
    {
        try
        {
            return await _repository.GetTagCountsAsync(RagFeature.KnowledgeBase, from: null, toExclusive: null, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Tagging still works without the library — the model just gets no reuse guidance this time.
            _logger.LogError(ex, "Failed to load KB question tag library; continuing with an empty library");
            return [];
        }
    }

    private static string Format(IReadOnlyList<RagTagCount> counts, RagTagKind kind)
    {
        var tags = counts
            .Where(c => c.Kind == kind)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Tag, StringComparer.Ordinal)
            .Take(MaxLibraryTagsPerKind)
            .Select(c => c.Tag)
            .ToList();

        return tags.Count == 0 ? EmptyLibraryText : string.Join(", ", tags);
    }
}
