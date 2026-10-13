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

        // Register services — the concrete type is the shared scoped root; both interfaces
        // alias to it so any resolution within one DI scope returns the same instance.
        services.AddScoped<GiftPackageManufactureService>();
        services.AddScoped<IGiftPackageManufactureService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
        services.AddScoped<IGiftPackageQueryService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());

        return services;
    }
}