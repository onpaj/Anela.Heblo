using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;

public class GetAdChangeHistoryRequest : IRequest<GetAdChangeHistoryResponse>
{
    public AdPlatform? Platform { get; set; }
    public string? AccountExternalId { get; set; }
    public AdChangeOrigin? Origin { get; set; }
    public AdChangeActorKind? ActorKind { get; set; }
    /// <summary>Default: To minus 30 days.</summary>
    public DateTimeOffset? From { get; set; }
    /// <summary>Default: now.</summary>
    public DateTimeOffset? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
