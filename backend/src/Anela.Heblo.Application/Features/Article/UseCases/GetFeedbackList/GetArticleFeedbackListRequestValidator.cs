using System.Linq;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;

public class GetArticleFeedbackListRequestValidator : AbstractValidator<GetArticleFeedbackListRequest>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    public GetArticleFeedbackListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .Must(AllowedPageSizes.Contains)
            .WithMessage($"PageSize must be one of: {string.Join(", ", AllowedPageSizes)}");
        RuleFor(x => x.SortBy)
            .Must(AllowedSortColumns.Contains)
            .WithMessage($"SortBy must be one of: {string.Join(", ", AllowedSortColumns)}");
    }
}
