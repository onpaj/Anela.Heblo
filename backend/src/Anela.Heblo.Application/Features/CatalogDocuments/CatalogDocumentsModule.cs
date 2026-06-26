using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CatalogDocuments;

public static class CatalogDocumentsModule
{
    public static IServiceCollection AddCatalogDocumentsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogDocumentsOptions>()
            .Bind(configuration.GetSection(CatalogDocumentsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new CatalogDocumentsOptions();
        configuration.GetSection(CatalogDocumentsOptions.SectionName).Bind(options);

        var drivesConfigured = !string.IsNullOrWhiteSpace(options.Materials.DriveId)
            && !options.Materials.DriveId.Contains("secrets.json")
            && !string.IsNullOrWhiteSpace(options.PIF.DriveId)
            && !options.PIF.DriveId.Contains("secrets.json");

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

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
