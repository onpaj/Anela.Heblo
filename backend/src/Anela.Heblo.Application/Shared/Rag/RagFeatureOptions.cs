using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Shared.Rag;

public abstract class RagFeatureOptions
{
    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    public int EmbeddingDimensions { get; set; } = 1536;

    [Required]
    public string ChatModel { get; set; } = "claude-sonnet-4-6";

    public int ChatMaxTokens { get; set; } = 1024;

    public int ChunkSize { get; set; } = 800;

    public int ChunkOverlap { get; set; } = 80;

    public double MinSimilarityScore { get; set; } = 0.55;

    [Required]
    public string IngestionCronExpression { get; set; } = "*/15 * * * *";

    [MinLength(1)]
    public List<OneDriveFolderMapping> OneDriveFolderMappings { get; set; } = [];

    public bool QueryExpansionEnabled { get; set; } = true;

    public string QueryExpansionModel { get; set; } = "claude-haiku-4-5-20251001";

    public string QueryExpansionPrompt { get; set; } = string.Empty;

    public RagQueryExpansionConfig ToExpansionConfig() =>
        new(QueryExpansionEnabled, QueryExpansionModel, QueryExpansionPrompt);

    /// <summary>
    /// Turns this feature's embedding config into the per-call options every
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> call site must pass, so a feature's
    /// own EmbeddingModel/EmbeddingDimensions reach the API instead of the adapter-wide fallback.
    /// </summary>
    public EmbeddingGenerationOptions ToEmbeddingOptions() =>
        new() { ModelId = EmbeddingModel, Dimensions = EmbeddingDimensions };
}
