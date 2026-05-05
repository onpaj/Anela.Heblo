using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence.Photobank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankModule
{
    public static IServiceCollection AddPhotobankModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPhotobankRepository, PhotobankRepository>();

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
