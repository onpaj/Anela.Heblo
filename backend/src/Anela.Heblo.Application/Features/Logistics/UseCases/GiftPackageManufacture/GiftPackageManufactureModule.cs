using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.GiftPackageManufacture;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;

public static class GiftPackageManufactureModule
{
    public static IServiceCollection AddGiftPackageManufactureModule(this IServiceCollection services)
    {
        // Register repository using factory pattern to avoid ServiceProvider antipattern
        services.AddScoped<IGiftPackageManufactureRepository>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            return new GiftPackageManufactureRepository(context);
        });

        // Register services
        services.AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>();

        return services;
    }
}