using Anela.Heblo.Application.Features.Photobank.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankModule
{
    public static IServiceCollection AddPhotobankModule(this IServiceCollection services, IConfiguration configuration)
    {
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwtValidation = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (!useMockAuth && !bypassJwtValidation)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IPhotobankGraphService, PhotobankGraphService>();
        }
        else
        {
            services.AddScoped<IPhotobankGraphService, MockPhotobankGraphService>();
        }

        return services;
    }
}
