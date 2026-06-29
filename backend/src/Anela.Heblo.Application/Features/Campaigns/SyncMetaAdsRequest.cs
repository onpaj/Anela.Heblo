using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns;

public record SyncMetaAdsRequest : IRequest
{
    /// <summary>Maximum campaigns to process in a single sync run. Prevents runaway API call bursts.</summary>
    public int MaxCampaigns { get; init; } = 50;

    /// <summary>Number of days back to fetch metrics for. Defaults to 30.</summary>
    public int SyncDaysBack { get; init; } = 30;
}
