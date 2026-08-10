using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Persistence.Attendance;
using FluentValidation;
using MediatR;
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
        services.AddScoped<Services.OvertimeCalculationService>();
        services.AddScoped<Services.OvertimeExcelBuilder>();
        services.AddScoped<Services.IOvertimeReportPublisher, Services.GraphOvertimeReportPublisher>();

        services.AddScoped<IOvertimeEmployeeRepository, OvertimeEmployeeRepository>();
        services.AddScoped<IOvertimeStatementRepository, OvertimeStatementRepository>();
        services.AddScoped<IOvertimeAdjustmentRepository, OvertimeAdjustmentRepository>();

        services.AddScoped<IValidator<UpsertOvertimeEmployeeRequest>, UpsertOvertimeEmployeeValidator>();
        services.AddScoped<IPipelineBehavior<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>,
            ValidationBehavior<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>>();

        services.AddScoped<IValidator<CreateAdjustmentRequest>, CreateAdjustmentValidator>();
        services.AddScoped<IPipelineBehavior<CreateAdjustmentRequest, CreateAdjustmentResponse>,
            ValidationBehavior<CreateAdjustmentRequest, CreateAdjustmentResponse>>();

        return services;
    }
}
