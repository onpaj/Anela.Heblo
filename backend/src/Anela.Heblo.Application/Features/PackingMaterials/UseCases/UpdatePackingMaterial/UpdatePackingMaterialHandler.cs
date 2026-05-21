using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;

public class UpdatePackingMaterialHandler : IRequestHandler<UpdatePackingMaterialRequest, UpdatePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public UpdatePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdatePackingMaterialResponse> Handle(
        UpdatePackingMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (material == null)
        {
            return new UpdatePackingMaterialResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.Id} not found."
            };
        }

        material.UpdateMaterial(request.Name, request.ConsumptionRate, request.ConsumptionType);
        await _repository.UpdateAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            ForecastedDays = null,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        return new UpdatePackingMaterialResponse
        {
            Material = materialDto
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}