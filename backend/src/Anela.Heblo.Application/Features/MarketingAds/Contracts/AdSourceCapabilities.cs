namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// What a read source can deliver. An unsupported capability makes the matching method return an
/// empty list (never throw). <paramref name="ChangeLogMaxAge"/> is how far back the platform's change
/// history reaches (Google: 30 days); null means unlimited and is only meaningful with a change log.
/// </summary>
public sealed record AdSourceCapabilities(bool SearchTerms, bool ChangeLog, TimeSpan? ChangeLogMaxAge);
