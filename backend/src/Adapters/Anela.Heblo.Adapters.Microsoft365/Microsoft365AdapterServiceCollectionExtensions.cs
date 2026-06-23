using Anela.Heblo.Adapters.Microsoft365.Photobank;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Microsoft365;

public static class Microsoft365AdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoft365Adapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);
        var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (useMockAuth || bypassJwt)
        {
            // Register mock GraphService for mock / dev authentication
            services.AddScoped<IGraphService, MockGraphService>();
        }
        else
        {
            services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
            services.AddHttpClient("MicrosoftGraph", _ => { })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                });
            services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();

            // Register real GraphService for production authentication
            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
            services.AddScoped<IGraphService, GraphService>();
        }

        return services;
    }
}
