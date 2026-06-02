using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Per-user async lock for serializing concurrent UserDashboardSettings mutations
        services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

        return services;
    }
}
