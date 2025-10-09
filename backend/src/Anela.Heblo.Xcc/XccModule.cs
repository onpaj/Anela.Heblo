using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Xcc;

/// <summary>
/// Cross-cutting concerns module registration
/// </summary>
public static class XccModule
{
    public static IServiceCollection AddXccServices(this IServiceCollection services)
    {
        // Register background refresh services
        services.AddBackgroundRefresh();

        return services;
    }
}