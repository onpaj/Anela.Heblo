namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>The ad group an ad belongs to and how many enabled ads that group has now (ads schema).</summary>
public sealed record AdGroupOccupancy(string AdGroupExternalId, int EnabledAdCount);
