using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionList;

public static class ExpeditionListModule
{
    public static IServiceCollection AddExpeditionListModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrintPickingListOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddScoped<IExpeditionListService, ExpeditionListService>();
        services.AddScoped<IValidator<PrintExpeditionOrderRequest>, PrintExpeditionOrderRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>,
            ValidationBehavior<PrintExpeditionOrderRequest, PrintExpeditionOrderResponse>>();

        // PrintPickingListJob is auto-discovered via IRecurringJob scan in AddRecurringJobs()

        return services;
    }
}
