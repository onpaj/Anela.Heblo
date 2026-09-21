using FluentValidation;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public class ListArticlesRequestValidator : AbstractValidator<ListArticlesRequest>
{
    public ListArticlesRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
