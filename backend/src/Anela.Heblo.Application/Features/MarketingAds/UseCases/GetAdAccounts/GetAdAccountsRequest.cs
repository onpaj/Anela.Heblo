using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;

public class GetAdAccountsRequest : IRequest<GetAdAccountsResponse>
{
    public AdPlatform? Platform { get; set; }
    public bool IncludeUnmanaged { get; set; }
}
