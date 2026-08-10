using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Persistence.Attendance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Attendance.Overtime;

public static class OvertimeModule
{
    public static IServiceCollection AddOvertimeModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OvertimeOptions>()
            .Bind(configuration.GetSection(OvertimeOptions.ConfigKey));

        services.AddScoped<IContractHoursProvider, Services.ConfigurationContractHoursProvider>();

        services.AddScoped<IOvertimeEmployeeRepository, OvertimeEmployeeRepository>();
        services.AddScoped<IOvertimeStatementRepository, OvertimeStatementRepository>();
        services.AddScoped<IOvertimeAdjustmentRepository, OvertimeAdjustmentRepository>();

        return services;
    }
}
