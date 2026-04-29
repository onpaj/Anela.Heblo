using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public interface IMarketingCategoryMapper
    {
        CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories);
        string MapToOutlookCategory(MarketingActionType actionType);
    }

    public sealed record CategoryMappingResult(
        MarketingActionType ActionType,
        string? MatchedCategory,
        IReadOnlyList<string> UnmappedCategories);
}
