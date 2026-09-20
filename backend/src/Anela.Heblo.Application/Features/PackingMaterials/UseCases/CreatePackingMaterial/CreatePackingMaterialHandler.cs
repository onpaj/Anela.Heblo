using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
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

        var materialDto = PackingMaterialMapper.ToDto(createdMaterial, forecastedDays: null); // New material, no history

        return new CreatePackingMaterialResponse
        {
            Id = createdMaterial.Id,
            Material = materialDto
        };
    }
}