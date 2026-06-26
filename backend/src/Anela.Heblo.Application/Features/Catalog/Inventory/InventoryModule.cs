using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Catalog.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Catalog.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddScoped<ILotRepository, LotRepository>();
        services.AddScoped<IMaterialContainerRepository, MaterialContainerRepository>();
        // IMaterialContainerCodeGenerator is registered in PersistenceModule: MaterialContainerCodeGenerator when a real
        // NpgsqlDataSource is available, NullMaterialContainerCodeGenerator when running in-memory.

        services.AddScoped<IValidator<CreateLotRequest>, CreateLotRequestValidator>();
        services.AddScoped<IValidator<UpdateLotRequest>, UpdateLotRequestValidator>();
        services.AddScoped<IValidator<CreateMaterialContainersRequest>, CreateMaterialContainersRequestValidator>();

        services.AddScoped<IPipelineBehavior<CreateLotRequest, CreateLotResponse>, ValidationBehavior<CreateLotRequest, CreateLotResponse>>();
        services.AddScoped<IPipelineBehavior<UpdateLotRequest, UpdateLotResponse>, ValidationBehavior<UpdateLotRequest, UpdateLotResponse>>();
        services.AddScoped<IPipelineBehavior<CreateMaterialContainersRequest, CreateMaterialContainersResponse>, ValidationBehavior<CreateMaterialContainersRequest, CreateMaterialContainersResponse>>();

        return services;
    }
}
