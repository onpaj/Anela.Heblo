using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public sealed record ContextSnippet
{
    public SourceType Source { get; init; }
    public string Title { get; init; } = "";
    public string Excerpt { get; init; } = "";
    public string? Url { get; init; }
    public Guid? ChunkId { get; init; }
    public double? Score { get; init; }
}
