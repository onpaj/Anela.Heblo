using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public sealed class ArticleListItemDto
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public ArticleStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
}
