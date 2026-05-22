using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CatalogDocuments;

public static class CatalogDocumentsModule
{
    public static IServiceCollection AddCatalogDocumentsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogDocumentsOptions>()
            .Bind(configuration.GetSection(CatalogDocumentsOptions.SectionName));

        var options = new CatalogDocumentsOptions();
        configuration.GetSection(CatalogDocumentsOptions.SectionName).Bind(options);

        var drivesConfigured = !string.IsNullOrWhiteSpace(options.Materials.DriveId)
            && !options.Materials.DriveId.Contains("secrets.json")
            && !string.IsNullOrWhiteSpace(options.PIF.DriveId)
            && !options.PIF.DriveId.Contains("secrets.json");

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (drivesConfigured && !useMockAuth && !bypassJwt)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddMemoryCache();
            services.AddScoped<ICatalogDocumentsStorage, GraphCatalogDocumentsStorage>();
        }
        else
        {
            services.AddScoped<ICatalogDocumentsStorage, NoOpCatalogDocumentsStorage>();
        }

        return services;
    }
}
