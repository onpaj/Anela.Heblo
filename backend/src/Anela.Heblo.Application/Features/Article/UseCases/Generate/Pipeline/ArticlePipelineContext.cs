using Anela.Heblo.Domain.Features.Article;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class ArticlePipelineContext
{
    public DomainArticle Article { get; init; } = null!;
    public List<string> SearchQueries { get; set; } = [];
    public List<ContextSnippet> ContextSnippets { get; set; } = [];
    public string? StyleGuideText { get; set; }
    public List<AggregatedFact> Facts { get; set; } = [];
    public string? GeneratedTitle { get; set; }
    public string? GeneratedHtml { get; set; }
    public List<(string Title, string? Url, SourceType Type)> SourceRefs { get; set; } = [];
}
