namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class UnresolvedArticleRow
{
    public Guid ArticleId { get; set; }
    public string OriginalValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
