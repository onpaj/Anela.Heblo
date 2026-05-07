using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;

public class GetAllocationsHandler : IRequestHandler<GetAllocationsRequest, GetAllocationsResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetAllocationsHandler> _logger;

    public GetAllocationsHandler(IPackingMaterialRepository repository, ILogger<GetAllocationsHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetAllocationsResponse> Handle(GetAllocationsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var material = await _repository.GetByIdWithAllocationsAsync(request.PackingMaterialId, cancellationToken);

            if (material == null)
            {
                return new GetAllocationsResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ResourceNotFound,
                    Error = $"Packing material with ID {request.PackingMaterialId} not found."
                };
            }

            var dtos = material.Allocations.Select(a => new PackingMaterialAllocationDto
            {
                Id = a.Id,
                PackingMaterialId = a.PackingMaterialId,
                ProductCode = a.ProductCode,
                AmountPerUnit = a.AmountPerUnit,
                CreatedAt = a.CreatedAt
            }).ToList();

            return new GetAllocationsResponse { Success = true, Allocations = dtos };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting allocations for packing material {PackingMaterialId}", request.PackingMaterialId);
            return new GetAllocationsResponse { Success = false, Error = "An unexpected error occurred while retrieving the allocations." };
        }
    }
}
