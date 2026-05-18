using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Persistence.Logistics.CarrierCooling;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CarrierCooling;

public static class CarrierCoolingModule
{
    public static IServiceCollection AddCarrierCoolingModule(this IServiceCollection services)
    {
        services.AddScoped<ICarrierCoolingRepository, CarrierCoolingRepository>();
        services.AddScoped<IValidator<SetCarrierCoolingRequest>, SetCarrierCoolingValidator>();

        return services;
    }
}
