using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Campaigns;

public static class CampaignsModule
{
    public static IServiceCollection AddCampaignsModule(this IServiceCollection services)
    {
        // MediatR handlers (SyncMetaAdsHandler) are auto-registered by MediatR assembly scan.
        // ICampaignRepository is registered by PersistenceModule.
        return services;
    }
}
