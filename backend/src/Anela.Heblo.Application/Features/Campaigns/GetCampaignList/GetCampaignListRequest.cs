using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignList;

public record GetCampaignListRequest(DateOnly From, DateOnly To, AdPlatform? Platform)
    : IRequest<IReadOnlyList<CampaignSummaryDto>>;
