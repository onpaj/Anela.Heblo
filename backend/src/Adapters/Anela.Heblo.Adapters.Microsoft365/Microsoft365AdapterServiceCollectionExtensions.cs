using Anela.Heblo.Application.Features.Marketing.Services;
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
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (!useMockAuth && !bypassJwt)
        {
            services.AddScoped<IOutlookCalendarSync, OutlookCalendarSyncService>();
        }

        return services;
    }
}
