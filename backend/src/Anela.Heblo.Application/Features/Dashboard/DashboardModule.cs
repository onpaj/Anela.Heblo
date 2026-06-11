using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Persistence.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IUserDashboardSettingsRepository, UserDashboardSettingsRepository>();

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Per-user async lock for serializing concurrent UserDashboardSettings mutations
        services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

        // Shared scaffold for Enable/Disable tile (and future) mutations
        services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>();

        return services;
    }
}
