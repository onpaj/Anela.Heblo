using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialHandler : IRequestHandler<CreatePackingMaterialRequest, CreatePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public CreatePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreatePackingMaterialResponse> Handle(
        CreatePackingMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var material = new PackingMaterial(
            request.Name,
            request.ConsumptionRate,
            request.ConsumptionType,
            request.CurrentQuantity);

        var createdMaterial = await _repository.AddAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var materialDto = new PackingMaterialDto
        {
            Id = createdMaterial.Id,
            Name = createdMaterial.Name,
            ConsumptionRate = createdMaterial.ConsumptionRate,
            ConsumptionType = createdMaterial.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(createdMaterial.ConsumptionType),
            CurrentQuantity = createdMaterial.CurrentQuantity,
            ForecastedDays = null, // New material, no history
            CreatedAt = createdMaterial.CreatedAt,
            UpdatedAt = createdMaterial.UpdatedAt
        };

        return new CreatePackingMaterialResponse
        {
            Id = createdMaterial.Id,
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