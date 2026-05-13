using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.GetCampaignDetail;

public class GetCampaignDetailHandler : IRequestHandler<GetCampaignDetailRequest, CampaignDetailDto>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignDetailHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<CampaignDetailDto> Handle(GetCampaignDetailRequest request, CancellationToken cancellationToken)
        => _repository.GetCampaignDetailAsync(request.CampaignId, request.From, request.To, cancellationToken);
}
