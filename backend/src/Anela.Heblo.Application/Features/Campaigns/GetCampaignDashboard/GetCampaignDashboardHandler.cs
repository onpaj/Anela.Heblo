using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignDashboard;

public class GetCampaignDashboardHandler : IRequestHandler<GetCampaignDashboardRequest, CampaignDashboardDto>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignDashboardHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<CampaignDashboardDto> Handle(GetCampaignDashboardRequest request, CancellationToken cancellationToken)
        => _repository.GetDashboardAsync(request.From, request.To, request.Platform, cancellationToken);
}
