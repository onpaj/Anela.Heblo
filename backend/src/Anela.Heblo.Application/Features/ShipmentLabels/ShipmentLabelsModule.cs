using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShipmentLabels;

public static class ShipmentLabelsModule
{
    public static IServiceCollection AddShipmentLabelsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IValidator<GetOrderShipmentLabelsRequest>, GetOrderShipmentLabelsRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>,
            ValidationBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>>();

        return services;
    }
}
