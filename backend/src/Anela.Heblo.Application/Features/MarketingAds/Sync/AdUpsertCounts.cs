namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Written = inserted + updated + deleted rows. Unmapped = rows whose entity Heblo does not know.</summary>
public sealed record AdUpsertCounts(int Written, int Unmapped);
