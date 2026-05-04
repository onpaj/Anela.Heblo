using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class ContextSnippet
{
    public SourceType Source { get; set; }
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string? Url { get; set; }
    public Guid? ChunkId { get; set; }
}
