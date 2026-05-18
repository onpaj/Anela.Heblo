using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Persistence.Logistics.CarrierCooling;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CarrierCooling;

public static class CarrierCoolingModule
{
    public static IServiceCollection AddCarrierCoolingModule(this IServiceCollection services)
    {
        services.AddScoped<ICarrierCoolingRepository, CarrierCoolingRepository>();
        services.AddScoped<IPipelineBehavior<SetCarrierCoolingRequest, SetCarrierCoolingResponse>, ValidationBehavior<SetCarrierCoolingRequest, SetCarrierCoolingResponse>>();

        return services;
    }
}
