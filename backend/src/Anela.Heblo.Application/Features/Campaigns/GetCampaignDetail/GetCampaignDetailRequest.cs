using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignDetail;

public record GetCampaignDetailRequest(Guid CampaignId, DateOnly From, DateOnly To)
    : IRequest<CampaignDetailDto>;
