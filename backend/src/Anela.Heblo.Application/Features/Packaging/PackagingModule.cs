using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.Packaging.Validators;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Packaging;

public static class PackagingModule
{
    public static IServiceCollection AddPackagingModule(this IServiceCollection services)
    {
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IPackageRepository, PackageRepository>();

        services.AddScoped<IValidator<ScanPackingOrderRequest>, ScanPackingOrderRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ScanPackingOrderRequest, ScanPackingOrderResponse>,
            ValidationBehavior<ScanPackingOrderRequest, ScanPackingOrderResponse>>();

        services.AddScoped<IValidator<GetPackagesRequest>, GetPackagesRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetPackagesRequest, GetPackagesResponse>,
            ValidationBehavior<GetPackagesRequest, GetPackagesResponse>>();

        return services;
    }
}
