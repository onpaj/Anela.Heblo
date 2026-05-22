using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Persistence.Logistics.GiftSettings;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GiftSettings;

public static class GiftSettingsModule
{
    public static IServiceCollection AddGiftSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGiftSettingRepository, GiftSettingRepository>();
        return services;
    }
}
