using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdateAllocation;

public class UpdateAllocationHandler : IRequestHandler<UpdateAllocationRequest, UpdateAllocationResponse>
{
    private readonly IPackingMaterialRepository _materialRepository;
    private readonly IPackingMaterialAllocationRepository _allocationRepository;
    private readonly ILogger<UpdateAllocationHandler> _logger;

    public UpdateAllocationHandler(
        IPackingMaterialRepository materialRepository,
        IPackingMaterialAllocationRepository allocationRepository,
        ILogger<UpdateAllocationHandler> logger)
    {
        _materialRepository = materialRepository;
        _allocationRepository = allocationRepository;
        _logger = logger;
    }

    public async Task<UpdateAllocationResponse> Handle(UpdateAllocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.AmountPerUnit <= 0)
                return new UpdateAllocationResponse { Success = false, Error = "AmountPerUnit must be greater than zero." };

            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return new UpdateAllocationResponse { Success = false, Error = "ProductCode must not be empty." };

            var material = await _materialRepository.GetByIdWithAllocationsAsync(request.PackingMaterialId, cancellationToken);

            if (material == null)
                return new UpdateAllocationResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Packing material with ID {request.PackingMaterialId} not found." };

            var allocation = material.Allocations.FirstOrDefault(a => a.Id == request.AllocationId);
            if (allocation == null)
                return new UpdateAllocationResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Allocation with ID {request.AllocationId} not found on this material." };

            var conflicting = material.Allocations
                .Any(a => a.Id != request.AllocationId && a.ProductCode == request.ProductCode);
            if (conflicting)
                return new UpdateAllocationResponse
                {
                    Success = false,
                    Error = $"An allocation for product '{request.ProductCode}' already exists on this material."
                };

            allocation.UpdateAllocation(request.ProductCode, request.AmountPerUnit);
            await _allocationRepository.UpdateAsync(allocation, cancellationToken);
            await _allocationRepository.SaveChangesAsync(cancellationToken);

            return new UpdateAllocationResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating allocation {AllocationId} for packing material {PackingMaterialId}", request.AllocationId, request.PackingMaterialId);
            return new UpdateAllocationResponse { Success = false, Error = "An unexpected error occurred while updating the allocation." };
        }
    }
}
