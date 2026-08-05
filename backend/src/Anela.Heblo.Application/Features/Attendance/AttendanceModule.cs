using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.Attendance;

public static class AttendanceModule
{
    public static IServiceCollection AddAttendanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BreakInsertionOptions>()
            .Bind(configuration.GetSection(BreakInsertionOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Services.BreakInsertionService>();

        // BreakInsertionJob is auto-discovered via the IRecurringJob assembly scan in AddRecurringJobs().

        return services;
    }
}
