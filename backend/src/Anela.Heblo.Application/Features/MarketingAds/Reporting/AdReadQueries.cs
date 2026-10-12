using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

public sealed record AdPerformanceQuery(AdPlatform Platform, string AccountExternalId, AdEntityLevel Level,
    DateOnly From, DateOnly To, string? ParentExternalId, int PageNumber, int PageSize);

public sealed record AdPerformancePage(string Currency, int TotalCount, IReadOnlyList<AdPerformanceRowDto> Rows);

public sealed record AdSearchTermQuery(AdPlatform Platform, string AccountExternalId, string? CampaignExternalId,
    string? AdGroupExternalId, DateOnly From, DateOnly To, decimal? MinCost, int Top);

public sealed record AdChangeEventQuery(AdPlatform? Platform, string? AccountExternalId, AdChangeOrigin? Origin,
    AdChangeActorKind? ActorKind, DateTimeOffset From, DateTimeOffset To, int PageNumber, int PageSize);

public sealed record AdChangeEventPage(int TotalCount, IReadOnlyList<AdChangeEventDto> Rows);
