using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.PackingMaterials;

public static class PackingMaterialsModule
{
    public static IServiceCollection AddPackingMaterialsModule(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IPackingMaterialRepository, PackingMaterialRepository>();
        services.AddScoped<IPackingMaterialAllocationRepository, PackingMaterialAllocationRepository>();

        // Register services
        services.AddScoped<IConsumptionCalculationService, ConsumptionCalculationService>();

        // Register Hangfire jobs
        services.AddScoped<DailyConsumptionJob>();

        // Register validators and validation pipeline behaviors
        services.AddScoped<IValidator<GetConsumptionHistoryRequest>, GetConsumptionHistoryRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetConsumptionHistoryRequest, GetConsumptionHistoryResponse>,
            ValidationBehavior<GetConsumptionHistoryRequest, GetConsumptionHistoryResponse>>();

        // MediatR handlers are automatically registered by MediatR scan

        return services;
    }
}