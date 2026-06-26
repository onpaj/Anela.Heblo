using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
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
        services.Configure<ShipmentLabelsSettings>(
            configuration.GetSection(ShipmentLabelsSettings.ConfigurationKey));

        services.AddScoped<IValidator<GetOrderShipmentLabelsRequest>, GetOrderShipmentLabelsRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>,
            ValidationBehavior<GetOrderShipmentLabelsRequest, GetOrderShipmentLabelsResponse>>();

        services.AddScoped<IValidator<CreateOrderShipmentRequest>, CreateOrderShipmentRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<CreateOrderShipmentRequest, CreateOrderShipmentResponse>,
            ValidationBehavior<CreateOrderShipmentRequest, CreateOrderShipmentResponse>>();

        // Named HttpClient used by GetPackageLabelPdfHandler to stream carrier-CDN PDFs
        // through our own origin so the SPA can silent-print without CORS errors.
        services.AddHttpClient(GetPackageLabelPdfHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
