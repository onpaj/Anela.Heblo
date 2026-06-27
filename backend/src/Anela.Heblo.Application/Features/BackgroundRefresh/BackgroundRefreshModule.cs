using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // No Application-layer services to register yet.
        // BackgroundRefreshController wires directly to IBackgroundRefreshTaskRegistry (Xcc).
        // MediatR handlers will be added here when the HTTP surface is migrated to CQRS.
        return services;
    }
}
