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

        services.AddOptions<AbsenceHoursOptions>()
            .Bind(configuration.GetSection(AbsenceHoursOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Services.BreakInsertionService>();
        services.AddScoped<Services.AbsenceHoursService>();

        // BreakInsertionJob and AbsenceHoursJob are auto-discovered via the IRecurringJob
        // assembly scan in AddRecurringJobs().

        return services;
    }
}
