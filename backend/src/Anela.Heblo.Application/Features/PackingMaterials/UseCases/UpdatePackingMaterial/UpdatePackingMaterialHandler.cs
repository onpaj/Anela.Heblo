using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
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

        var materialDto = PackingMaterialMapper.ToDto(material, forecastedDays: null);

        return new UpdatePackingMaterialResponse
        {
            Material = materialDto
        };
    }
}