using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public sealed record ArticleSourceRef(
    string Title,
    string? Url,
    SourceType Type,
    Guid? ChunkId,
    double? Confidence,
    string? Excerpt,
    string? ValidationNote);
