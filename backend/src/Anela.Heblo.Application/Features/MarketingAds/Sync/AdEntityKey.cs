using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Natural key of an ad entity inside one account: platforms only guarantee id uniqueness per level.</summary>
public readonly record struct AdEntityKey(AdEntityLevel Level, string ExternalId);
