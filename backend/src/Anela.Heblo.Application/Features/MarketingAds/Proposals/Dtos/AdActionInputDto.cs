using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

/// <summary>
/// One proposed action as sent by an agent or a person. Old/new values are not accepted from the
/// client — Heblo fills them from the action type's convention (spec 12.2).
/// </summary>
public class AdActionInputDto
{
    public AdEntityLevel TargetLevel { get; set; }
    public string TargetExternalId { get; set; } = string.Empty;
    /// <summary>AddNegativeKeyword only.</summary>
    public string? KeywordText { get; set; }
    /// <summary>AddNegativeKeyword only.</summary>
    public KeywordMatchType? MatchType { get; set; }
}
