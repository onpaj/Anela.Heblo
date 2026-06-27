using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundRefresh;

public static class BackgroundRefreshModule
{
    public static IServiceCollection AddBackgroundRefreshModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the assembly scan in ApplicationModule.
        // IBackgroundRefreshTaskRegistry is registered as a singleton by XccModule — do not re-register.
        return services;
    }
}
