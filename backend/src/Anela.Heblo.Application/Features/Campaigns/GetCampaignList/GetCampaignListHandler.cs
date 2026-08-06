using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignList;

public class GetCampaignListHandler : IRequestHandler<GetCampaignListRequest, IReadOnlyList<CampaignSummaryDto>>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignListHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<CampaignSummaryDto>> Handle(GetCampaignListRequest request, CancellationToken cancellationToken)
        => _repository.GetCampaignListAsync(request.From, request.To, request.Platform, cancellationToken);
}
