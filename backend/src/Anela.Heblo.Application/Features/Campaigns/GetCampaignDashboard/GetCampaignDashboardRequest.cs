using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignDashboard;

public record GetCampaignDashboardRequest(DateOnly From, DateOnly To, AdPlatform? Platform)
    : IRequest<CampaignDashboardDto>;
