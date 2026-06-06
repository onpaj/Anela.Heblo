using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence.Features.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Authorization;

public static class AuthorizationModule
{
    public static IServiceCollection AddAuthorizationModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        return services;
    }
}
