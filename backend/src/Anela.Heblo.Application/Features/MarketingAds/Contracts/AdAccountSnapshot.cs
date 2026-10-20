namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary><paramref name="Currency"/> is an ISO 4217 code (e.g. "CZK"); <paramref name="TimeZone"/> an IANA id.</summary>
public sealed record AdAccountSnapshot(string ExternalId, string Name, string Currency, string TimeZone);
