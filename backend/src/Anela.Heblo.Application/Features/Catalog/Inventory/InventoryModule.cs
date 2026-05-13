using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
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
        services.AddScoped<IEanRepository, EanRepository>();
        // IEanCodeGenerator is registered in PersistenceModule: EanCodeGenerator when a real
        // NpgsqlDataSource is available, NullEanCodeGenerator when running in-memory.

        services.AddScoped<IValidator<CreateLotRequest>, CreateLotRequestValidator>();
        services.AddScoped<IValidator<UpdateLotRequest>, UpdateLotRequestValidator>();
        services.AddScoped<IValidator<CreateEansRequest>, CreateEansRequestValidator>();

        services.AddScoped<IPipelineBehavior<CreateLotRequest, CreateLotResponse>, ValidationBehavior<CreateLotRequest, CreateLotResponse>>();
        services.AddScoped<IPipelineBehavior<UpdateLotRequest, UpdateLotResponse>, ValidationBehavior<UpdateLotRequest, UpdateLotResponse>>();
        services.AddScoped<IPipelineBehavior<CreateEansRequest, CreateEansResponse>, ValidationBehavior<CreateEansRequest, CreateEansResponse>>();

        return services;
    }
}
