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
        services.AddScoped<IOvertimeEmployeeRepository, OvertimeEmployeeRepository>();
        services.AddScoped<IOvertimeStatementRepository, OvertimeStatementRepository>();
        services.AddScoped<IOvertimeAdjustmentRepository, OvertimeAdjustmentRepository>();

        return services;
    }
}
