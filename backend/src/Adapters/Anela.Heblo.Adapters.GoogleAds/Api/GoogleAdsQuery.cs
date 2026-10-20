namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>A GAQL query plus a stable name used in logs and to route test fixtures.</summary>
internal sealed record GoogleAdsQuery(string Name, string Gaql);
