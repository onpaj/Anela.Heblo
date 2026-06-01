using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;

public class CreateAllocationHandler : IRequestHandler<CreateAllocationRequest, CreateAllocationResponse>
{
    private readonly IPackingMaterialRepository _materialRepository;
    private readonly IPackingMaterialAllocationRepository _allocationRepository;
    private readonly ILogger<CreateAllocationHandler> _logger;

    public CreateAllocationHandler(
        IPackingMaterialRepository materialRepository,
        IPackingMaterialAllocationRepository allocationRepository,
        ILogger<CreateAllocationHandler> logger)
    {
        _materialRepository = materialRepository;
        _allocationRepository = allocationRepository;
        _logger = logger;
    }

    public async Task<CreateAllocationResponse> Handle(CreateAllocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.AmountPerUnit <= 0)
                return new CreateAllocationResponse { Success = false, Error = "AmountPerUnit must be greater than zero." };

            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return new CreateAllocationResponse { Success = false, Error = "ProductCode must not be empty." };

            var material = await _materialRepository.GetByIdWithAllocationsAsync(request.PackingMaterialId, cancellationToken);

            if (material == null)
                return new CreateAllocationResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Packing material with ID {request.PackingMaterialId} not found." };

            var duplicate = material.Allocations.Any(a => a.ProductCode == request.ProductCode);
            if (duplicate)
                return new CreateAllocationResponse { Success = false, Error = $"An allocation for product '{request.ProductCode}' already exists on this material." };

            var allocation = new PackingMaterialAllocation(request.PackingMaterialId, request.ProductCode, request.AmountPerUnit);
            var created = await _allocationRepository.AddAsync(allocation, cancellationToken);
            await _allocationRepository.SaveChangesAsync(cancellationToken);

            var dto = new PackingMaterialAllocationDto
            {
                Id = created.Id,
                PackingMaterialId = created.PackingMaterialId,
                ProductCode = created.ProductCode,
                AmountPerUnit = created.AmountPerUnit,
                CreatedAt = created.CreatedAt
            };

            return new CreateAllocationResponse { Success = true, Allocation = dto };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating allocation for packing material {PackingMaterialId}", request.PackingMaterialId);
            return new CreateAllocationResponse { Success = false, Error = "An unexpected error occurred while creating the allocation." };
        }
    }
}
